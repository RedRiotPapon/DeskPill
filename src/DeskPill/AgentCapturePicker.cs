using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;

namespace DeskPill
{
    // Only a selection overlay. No screenshot is taken until the user commits a region.
    internal sealed class AgentCapturePicker : Window
    {
        readonly TaskCompletionSource<Rectangle?> completion = new TaskCompletionSource<Rectangle?>();
        readonly Canvas canvas = new Canvas();
        readonly System.Windows.Shapes.Rectangle selection = new System.Windows.Shapes.Rectangle();
        Rectangle desktop;
        AgentVisionNative.NativePoint origin;
        bool dragging;
        internal Task<Rectangle?> Completion { get { return completion.Task; } }
        internal AgentCapturePicker()
        {
            using (new AgentPhysicalCoordinates()) desktop = AgentVisionNative.VirtualBounds();
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
            Topmost = true; AllowsTransparency = true; Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 0, 0, 0));
            Cursor = Cursors.Cross; Content = canvas;
            Left = SystemParameters.VirtualScreenLeft; Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth; Height = SystemParameters.VirtualScreenHeight;
            canvas.Background = Brushes.Transparent;
            selection.Stroke = Brushes.LimeGreen; selection.StrokeThickness = 2;
            canvas.Children.Add(selection);
            var help = new TextBlock { Text = "Drag to attach a screen area · Esc to cancel", Foreground = Brushes.White,
                Background = Brushes.Black, FontSize = 18, Padding = new Thickness(12) };
            Canvas.SetLeft(help, 30); Canvas.SetTop(help, 30); canvas.Children.Add(help);
            SourceInitialized += delegate
            {
                using (new AgentPhysicalCoordinates()) AgentVisionNative.SetWindowPos(new WindowInteropHelper(this).Handle,
                    new IntPtr(-1), desktop.X, desktop.Y, desktop.Width, desktop.Height, 0x0040);
            };
            MouseLeftButtonDown += delegate
            {
                using (new AgentPhysicalCoordinates()) AgentVisionNative.GetCursorPos(out origin);
                dragging = true; CaptureMouse();
            };
            MouseMove += delegate
            {
                if (!dragging) return;
                AgentVisionNative.NativePoint point;
                using (new AgentPhysicalCoordinates()) AgentVisionNative.GetCursorPos(out point);
                Rectangle rect = SelectionBounds(origin.X, origin.Y, point.X, point.Y, desktop);
                double sx = canvas.ActualWidth / desktop.Width, sy = canvas.ActualHeight / desktop.Height;
                Canvas.SetLeft(selection, (rect.Left - desktop.Left) * sx); Canvas.SetTop(selection, (rect.Top - desktop.Top) * sy);
                selection.Width = rect.Width * sx; selection.Height = rect.Height * sy;
            };
            MouseLeftButtonUp += delegate
            {
                if (!dragging) return;
                dragging = false; ReleaseMouseCapture();
                AgentVisionNative.NativePoint point;
                using (new AgentPhysicalCoordinates()) AgentVisionNative.GetCursorPos(out point);
                Rectangle rect = SelectionBounds(origin.X, origin.Y, point.X, point.Y, desktop);
                Close();
                completion.TrySetResult(rect.Width > 2 && rect.Height > 2 ? (Rectangle?)rect : null);
            };
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); };
            // Complete on dispatcher after MouseUp has supplied its committed selection.
            Closed += delegate { Dispatcher.BeginInvoke(new Action(delegate { completion.TrySetResult(null); })); };
        }
        internal static Rectangle SelectionBounds(int x1, int y1, int x2, int y2, Rectangle desktop)
        { return Rectangle.Intersect(desktop, Rectangle.FromLTRB(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2))); }
    }
}
