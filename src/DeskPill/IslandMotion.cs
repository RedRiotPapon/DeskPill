using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DeskPill
{
    public enum NotchPose
    {
        Tiny,
        Compact,
        HudPill,
        NotificationPeek,
        Expanded,
        OptionsDock,
        DropTarget,
        InlineAsk,
        InlineToday,
        InlineLyrics
    }

    // - Open:      stiffness=300, damping=25.63 (zeta ~0.74, crisp bloom with subtle overshoot)
    // - Close:     stiffness=420, damping=32.79 (zeta ~0.80, swift clean tuck-in)
    // - Tiny:      stiffness=200, damping=29.00 (critically damped shrink when browser tabs overlap)
    // - Re-expand: stiffness=230, damping=19.00 (zeta ~0.63, plush organic hover morph)
    // - PageSlide: stiffness=360, damping=30.00 (continuous horizontal spring carousel)
    // Uses RenderingTime deduplication, elapsed-time spring integration and detaches at rest.
    sealed class IslandMotion
    {
        internal const double CanvasWidth = 540.0;
        internal const double CanvasHeight = 276.0;
        internal const double TinyWidth = 52.0;
        internal const double TinyHeight = 6.0;
        internal const double CompactHeight = 30.0;
        internal const double HudPillWidth = 296.0;
        internal const double HudPillHeight = 38.0;
        internal const double ExpandedHeight = 172.0;
        internal const double OptionsDockHeight = 202.0;
        internal const double DropTargetHeight = 188.0;
        internal const double InlineAskHeight = 228.0;
        internal const double InlineTodayHeight = 238.0;
        internal const double InlineLyricsHeight = 238.0;
        internal const double FloatingTopOffset = 10.0;

        readonly Path shell;
        readonly Path rim;
        readonly Canvas content;
        readonly Stopwatch clock = new Stopwatch();
        double lastTickSeconds;
        double lastDrawnWidth = double.NaN, lastDrawnHeight;
        bool lastDrawnFloating;
        TimeSpan lastRenderTime = TimeSpan.MinValue;
        bool subscribed;
        bool floating;
        int generation;
        Action onSettled;
        readonly Action onFrame;

        // Primary width/height springs (logical DIPs before window scale).
        double curW = 396.0, velW, targetW = 396.0;
        double curH = ExpandedHeight, velH, targetH = ExpandedHeight;
        double stiff = 230.0, damp = 19.0;

        // Options dock reveal spring (0 = dots, 1 = icon dock).
        double dockPos, dockVel, dockTarget;
        double contentPos = 1.0, contentVel, contentTarget = 1.0;

        // Sleek HUD pill reveal spring (0 = hidden, 1 = HUD overlay).
        double hudPos, hudVel, hudTarget;

        // Smooth rolling HUD meter/number spring (0..1, critically damped for zero jump/delay).
        double hudMeterPos, hudMeterVel, hudMeterTarget;

        // Horizontal page-carousel spring chasing 0.0.
        double pageSlideX, pageSlideVel;

        public bool IsAnimating { get; private set; }
        public double CurrentWidth { get { return curW; } }
        public double CurrentHeight { get { return curH; } }
        public double TargetWidth { get { return targetW; } }
        public double TargetHeight { get { return targetH; } }
        public double DockReveal { get { return Clamp01(dockPos); } }
        public double HudReveal { get { return Clamp01(hudPos); } }
        public double HudMeter { get { return Clamp01(hudMeterPos); } }
        public double PageSlideX { get { return pageSlideX; } }
        public double PageSlideProgress { get { return Clamp01(1.0 - Math.Abs(pageSlideX) / 140.0); } }
        public double TopOffset { get { return floating ? FloatingTopOffset : 0.0; } }
        public double Progress { get { return Clamp01((curH - CompactHeight) / (ExpandedHeight - CompactHeight)); } }

        // It starts fading on the first frame instead of popping through a width threshold.
        public double ExpandedReveal { get { return Clamp01(contentPos); } }

        public double CompactReveal
        {
            get
            {
                double fromTiny = Clamp01((curH - 12.0) / 14.0);
                fromTiny = fromTiny * fromTiny * (3.0 - 2.0 * fromTiny);
                double toExpanded = Clamp01(1.0 - Progress / 0.32);
                toExpanded = toExpanded * toExpanded * (3.0 - 2.0 * toExpanded);
                return fromTiny * toExpanded * Clamp01(1.0 - HudReveal * 1.8);
            }
        }

        public double TinyIndicatorReveal
        {
            get
            {
                double u = Clamp01(1.0 - (curH - 7.0) / 10.0);
                return u * u * (3.0 - 2.0 * u);
            }
        }

        public double ExpandedScale
        {
            get
            {
                double s = Clamp01((Progress - 0.08) / 0.78);
                s = s * s * (3.0 - 2.0 * s);
                return 0.94 + 0.06 * s;
            }
        }

        public double ExpandedTranslateY
        {
            get
            {
                double s = Clamp01((Progress - 0.08) / 0.78);
                s = s * s * (3.0 - 2.0 * s);
                return (1.0 - s) * -8.0;
            }
        }

        public IslandMotion(Path shell, Path rim, Canvas content, Action onFrame)
        {
            this.shell = shell;
            this.rim = rim;
            this.content = content;
            this.onFrame = onFrame;
        }

        public void SetStyle(bool value)
        {
            if (floating == value && shell != null && shell.Data != null)
                return;
            floating = value;
            Draw();
        }

        public void Snap(double width, double height, bool showDock)
        {
            SnapPose(width, height, showDock, false);
        }

        public void SnapPose(double width, double height, bool showDock, bool showHud)
        {
            generation++;
            StopLoop();
            curW = targetW = width;
            curH = targetH = height;
            velW = velH = 0;
            dockPos = dockTarget = showDock ? 1.0 : 0.0;
            dockVel = 0;
            hudPos = hudTarget = showHud ? 1.0 : 0.0;
            hudVel = 0;
            contentPos = contentTarget = height >= ExpandedHeight ? 1.0 : 0.0;
            contentVel = 0;
            pageSlideX = pageSlideVel = 0;
            IsAnimating = false;
            onSettled = null;
            Draw();
            if (onFrame != null)
                onFrame();
        }

        public void Finish(bool expanded)
        {
            Snap(expanded ? (floating ? 380.0 : 396.0) : 172.0, expanded ? ExpandedHeight : CompactHeight, false);
        }

        public void KickPageSlide(double initialOffset)
        {
            pageSlideX = initialOffset;
            pageSlideVel = 0;
            IsAnimating = true;
            StartLoop();
            if (onFrame != null)
                onFrame();
        }

        public void ClearPageSlide()
        {
            pageSlideX = pageSlideVel = 0;
        }

        public void SetHudMeterTarget(double target, double startFrom, bool snapNow)
        {
            double clamped = Clamp01(target);
            if (snapNow)
            {
                hudMeterPos = hudMeterTarget = clamped;
                hudMeterVel = 0;
                if (onFrame != null)
                    onFrame();
                return;
            }

            if (startFrom >= 0.0)
            {
                hudMeterPos = Clamp01(startFrom);
                hudMeterVel = 0;
            }
            hudMeterTarget = clamped;
            if (Math.Abs(hudMeterPos - hudMeterTarget) > 0.001 || Math.Abs(hudMeterVel) > 0.01)
            {
                IsAnimating = true;
                StartLoop();
                if (onFrame != null)
                    onFrame();
            }
        }

        public void MoveTo(double width, double height, bool showDock, bool opening, bool closing, bool immediate, Action completed)
        {
            MoveToPose(width, height, showDock, false, opening, closing, false, immediate, completed);
        }

        public void MoveToPose(double width, double height, bool showDock, bool showHud, bool opening, bool closing, bool shrinkingToTiny, bool immediate, Action completed)
        {
            int version = ++generation;
            targetW = width;
            targetH = height;
            dockTarget = showDock ? 1.0 : 0.0;
            hudTarget = showHud ? 1.0 : 0.0;
            contentTarget = height >= ExpandedHeight ? 1.0 : 0.0;

            if (immediate)
            {
                SnapPose(width, height, showDock, showHud);
                if (completed != null)
                    completed();
                return;
            }

            if (shrinkingToTiny)
            {
                // Critically damped smooth tuck to tiny pill (zero bounce over browser tabs).
                stiff = 200.0;
                damp = 29.0;
            }
            else if (closing)
            {

                stiff = 420.0;
                damp = 32.79;
            }
            else if (opening)
            {

                stiff = 300.0;
                damp = 25.63;
            }
            else if (showHud)
            {
                stiff = 240.0;
                damp = 26.0;
            }
            else
            {

                stiff = 230.0;
                damp = 19.0;
            }

            onSettled = delegate
            {
                if (version != generation)
                    return;
                if (completed != null)
                    completed();
            };

            if (IsSettled())
            {
                SnapPose(width, height, showDock, showHud);
                if (completed != null)
                    completed();
                return;
            }

            IsAnimating = true;
            StartLoop();
        }

        public void Stop()
        {
            generation++;
            StopLoop();
            IsAnimating = false;
            onSettled = null;
        }

        public Rect CurrentBounds(double pad)
        {
            double w = Math.Max(36.0, curW);
            double h = Math.Max(TinyHeight, curH);
            double ear = floating ? 0.0 : EarRadius(h);
            double left = (CanvasWidth - w) * 0.5 - ear - pad;
            double right = (CanvasWidth + w) * 0.5 + ear + pad;
            double top = TopOffset;
            return new Rect(Math.Max(0, left), top, Math.Min(CanvasWidth, right) - Math.Max(0, left), h + pad);
        }

        void StartLoop()
        {
            if (subscribed)
                return;
            clock.Restart();
            lastTickSeconds = 0;
            lastRenderTime = TimeSpan.MinValue;
            CompositionTarget.Rendering += OnRenderTick;
            subscribed = true;
        }

        void StopLoop()
        {
            if (!subscribed)
                return;
            CompositionTarget.Rendering -= OnRenderTick;
            subscribed = false;
            clock.Stop();
        }

        void OnRenderTick(object sender, EventArgs e)
        {
            var args = e as RenderingEventArgs;
            if (args != null)
            {
                if (args.RenderingTime == lastRenderTime)
                    return;
                lastRenderTime = args.RenderingTime;
            }

            double now = clock.Elapsed.TotalSeconds;
            double dt = now - lastTickSeconds;
            lastTickSeconds = now;
            if (dt <= 0)
                return;
            // Integrate elapsed wall time. Adaptive dt smoothing made dropped frames
            // slow the animation clock and vary settling time with refresh rate.
            dt = Math.Min(dt, 0.1);

            StepPhysics(dt);
            Draw();
            if (onFrame != null)
                onFrame();

            if (IsSettled())
            {
                Action callback = onSettled;
                onSettled = null;
                curW = targetW;
                curH = targetH;
                velW = velH = 0;
                dockPos = dockTarget;
                dockVel = 0;
                hudPos = hudTarget;
                hudVel = 0;
                hudMeterPos = hudMeterTarget;
                hudMeterVel = 0;
                pageSlideX = pageSlideVel = 0;
                contentPos = contentTarget;
                contentVel = 0;
                IsAnimating = false;
                StopLoop();
                Draw();
                if (onFrame != null)
                    onFrame();
                if (callback != null)
                    callback();
            }
        }

        public double TimeScale = 1.0;

        internal void StepPhysics(double dt)
        {
            dt *= Math.Max(.7,Math.Min(1.3,TimeScale));
            // Sub-step to <= 4ms to keep spring integration stable across frame cadences.
            int steps = Math.Max(1, (int)Math.Ceiling(dt / 0.004));
            double h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                double aw = -stiff * (curW - targetW) - damp * velW;
                velW += aw * h;
                curW += velW * h;

                double ah = -stiff * (curH - targetH) - damp * velH;
                velH += ah * h;
                curH += velH * h;

                if (curW < 36.0) { curW = 36.0; if (velW < 0) velW = 0; }
                if (curW > CanvasWidth - 20.0) { curW = CanvasWidth - 20.0; if (velW > 0) velW = 0; }
                if (curH < 4.0) { curH = 4.0; if (velH < 0) velH = 0; }
                if (curH > CanvasHeight - 16.0) { curH = CanvasHeight - 16.0; if (velH > 0) velH = 0; }

                double ck = contentTarget > 0.5 ? 260.0 : 520.0;
                double cd = contentTarget > 0.5 ? 32.2 : 45.6;
                contentVel += (-ck * (contentPos - contentTarget) - cd * contentVel) * h;
                contentPos += contentVel * h;

                // Options dock reveal spring (360/30 open, 500/43 close).
                double dk = dockTarget < dockPos ? 500.0 : 360.0;
                double dd = dockTarget < dockPos ? 43.0 : 30.0;
                dockVel += (-dk * (dockPos - dockTarget) - dd * dockVel) * h;
                dockPos += dockVel * h;

                // HUD pill overlay reveal spring (360/30 open, 480/40 close).
                double hk = hudTarget < hudPos ? 480.0 : 360.0;
                double hd = hudTarget < hudPos ? 40.0 : 30.0;
                hudVel += (-hk * (hudPos - hudTarget) - hd * hudVel) * h;
                hudPos += hudVel * h;

                // Critically damped HUD meter/number rolling spring (260/32 -> ~160ms butter-smooth roll).
                if (Math.Abs(hudMeterPos - hudMeterTarget) > 0.0005 || Math.Abs(hudMeterVel) > 0.005)
                {
                    double am = -260.0 * (hudMeterPos - hudMeterTarget) - 32.0 * hudMeterVel;
                    hudMeterVel += am * h;
                    hudMeterPos += hudMeterVel * h;
                }

                // Continuous horizontal page-carousel spring chasing 0.0 (360/30).
                if (Math.Abs(pageSlideX) > 0.001 || Math.Abs(pageSlideVel) > 0.001)
                {
                    double ax = -360.0 * pageSlideX - 30.0 * pageSlideVel;
                    pageSlideVel += ax * h;
                    pageSlideX += pageSlideVel * h;
                }
            }
        }

        bool IsSettled()
        {
            return Math.Abs(contentPos - contentTarget) < 0.001 && Math.Abs(contentVel) < 0.03
                && Math.Abs(curW - targetW) < 0.15
                && Math.Abs(velW) < 1.0
                && Math.Abs(curH - targetH) < 0.15
                && Math.Abs(velH) < 1.0
                && Math.Abs(dockPos - dockTarget) < 0.004
                && Math.Abs(dockVel) < 0.05
                && Math.Abs(hudPos - hudTarget) < 0.004
                && Math.Abs(hudVel) < 0.05
                && Math.Abs(hudMeterPos - hudMeterTarget) < 0.002
                && Math.Abs(hudMeterVel) < 0.01
                && Math.Abs(pageSlideX) < 0.25
                && Math.Abs(pageSlideVel) < 2.0;
        }

        static double Clamp01(double value)
        {
            return value < 0.0 ? 0.0 : (value > 1.0 ? 1.0 : value);
        }

        static double EarRadius(double height)
        {
            double hScale = Clamp01((height - 4.0) / 18.0);
            double t = Clamp01((height - CompactHeight) / 110.0);
            return Math.Min((11.0 + 7.0 * t) * hScale, height * 0.45);
        }

        void Draw()
        {
            if (shell == null || content == null)
                return;
            if (Math.Abs(curW - lastDrawnWidth) < 0.0001 && Math.Abs(curH - lastDrawnHeight) < 0.0001 && floating == lastDrawnFloating)
                return;
            lastDrawnWidth = curW;
            lastDrawnHeight = curH;
            lastDrawnFloating = floating;
            Geometry geometry = OutlineSize(curW, curH, floating);
            shell.Data = geometry;
            content.Clip = geometry;
            if (rim != null)
            {
                rim.Data = geometry;
                double t = Clamp01((curH - CompactHeight) / 110.0);
                double hScale = Clamp01((curH - 4.0) / 18.0);
                rim.Opacity = (floating ? (0.30 + 0.25 * t) : (0.04 + 0.18 * t)) * hScale;
            }
        }

        // Kept for compatibility with symmetry / geometry probes across progress [0..1].
        internal static Geometry Outline(double progress, bool floating)
        {
            double p = Clamp01(progress);
            double w = 172.0 + (floating ? 208.0 : 224.0) * p;
            double h = CompactHeight + (ExpandedHeight - CompactHeight) * p;
            return OutlineSize(w, h, floating);
        }

        internal static Geometry OutlineSize(double width, double height, bool floating)
        {
            double w = Math.Max(36.0, Math.Min(CanvasWidth - 20.0, width));
            double h = Math.Max(4.0, Math.Min(CanvasHeight - 12.0, height));
            double y0 = floating ? FloatingTopOffset : 0.0;
            double y1 = y0 + h;
            double left = (CanvasWidth - w) * 0.5;
            double right = left + w;
            double microT = Clamp01((h - TinyHeight) / (CompactHeight - TinyHeight));
            double t = Clamp01((h - CompactHeight) / 110.0);

            double bottomR = Math.Min(3.0 + 11.0 * microT + 16.0 * t, Math.Min(h * 0.5, w * 0.5));
            var geometry = new StreamGeometry();
            using (var c = geometry.Open())
            {
                if (floating)
                {
                    double topR = Math.Min(3.0 + 11.0 * microT + 14.0 * t, Math.Min(h * 0.5, w * 0.5));
                    c.BeginFigure(new Point(left + topR, y0), true, true);
                    c.LineTo(new Point(right - topR, y0), true, false);
                    c.QuadraticBezierTo(new Point(right, y0), new Point(right, y0 + topR), true, false);
                    c.LineTo(new Point(right, y1 - bottomR), true, false);
                    c.QuadraticBezierTo(new Point(right, y1), new Point(right - bottomR, y1), true, false);
                    c.LineTo(new Point(left + bottomR, y1), true, false);
                    c.QuadraticBezierTo(new Point(left, y1), new Point(left, y1 - bottomR), true, false);
                    c.LineTo(new Point(left, y0 + topR), true, false);
                    c.QuadraticBezierTo(new Point(left, y0), new Point(left + topR, y0), true, false);
                }
                else
                {
                    // Classic top-bezel notch with smooth C1-continuous concave top shoulders.
                    double ear = EarRadius(h);
                    double earDrop = Math.Min(ear, h - bottomR);
                    if (earDrop < 1.5)
                        earDrop = Math.Min(ear, h * 0.45);
                    c.BeginFigure(new Point(left - ear, 0), true, true);
                    c.LineTo(new Point(right + ear, 0), true, false);
                    c.BezierTo(new Point(right + ear * 0.35, 0), new Point(right, earDrop * 0.25), new Point(right, earDrop), true, false);
                    c.LineTo(new Point(right, y1 - bottomR), true, false);
                    c.QuadraticBezierTo(new Point(right, y1), new Point(right - bottomR, y1), true, false);
                    c.LineTo(new Point(left + bottomR, y1), true, false);
                    c.QuadraticBezierTo(new Point(left, y1), new Point(left, y1 - bottomR), true, false);
                    c.LineTo(new Point(left, earDrop), true, false);
                    c.BezierTo(new Point(left, earDrop * 0.25), new Point(left - ear * 0.35, 0), new Point(left - ear, 0), true, false);
                }
            }

            geometry.Freeze();
            return geometry;
        }
    }
}
