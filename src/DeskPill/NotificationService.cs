using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DeskPill
{
    public sealed class DesktopNotification
    {
        public string Key = "", AppId = "", AppName = "Notification", Title = "", Body = "";
        public DateTimeOffset CreatedAt;
    }

    // In-memory arrival tracking: never persists notification contents or replays history.
    public sealed class NotificationInbox
    {
        readonly Dictionary<string, DateTimeOffset> seen = new Dictionary<string, DateTimeOffset>();
        bool primed;
        public void Reset() { seen.Clear(); primed = false; }
        public List<DesktopNotification> Observe(IEnumerable<DesktopNotification> snapshot, DateTimeOffset now)
        {
            var arrivals = new List<DesktopNotification>();
            foreach (var item in snapshot.OrderBy(n => n.CreatedAt))
            {
                if (string.IsNullOrEmpty(item.Key) || seen.ContainsKey(item.Key)) continue;
                seen[item.Key] = now;
                if (primed && item.CreatedAt >= now.AddSeconds(-30) && item.CreatedAt <= now.AddSeconds(5)) arrivals.Add(item);
            }
            primed = true;
            if (seen.Count > 2048)
                foreach (string key in seen.OrderBy(p => p.Value).Take(seen.Count - 1024).Select(p => p.Key).ToArray()) seen.Remove(key);
            return arrivals.Skip(Math.Max(0, arrivals.Count - 10)).ToList();
        }
    }

    public sealed class NotificationService : IDisposable
    {
        readonly NotificationInbox inbox = new NotificationInbox();
        int polling;
        bool disposed;
        public string Status { get; private set; }
        public NotificationService() { Status = "Waiting for notification access"; }

        // Polling deliberately avoids NotificationChanged, which is unreliable in unpackaged
        // desktop apps. Only the supported notification-list API is used; no database access.
        public async Task<List<DesktopNotification>> PollAsync(bool enabled)
        {
            if (disposed || Interlocked.CompareExchange(ref polling, 1, 0) != 0) return new List<DesktopNotification>();
            try
            {
                if (!enabled) { inbox.Reset(); Status = "Notification mirroring is off"; return new List<DesktopNotification>(); }
                var listener = UserNotificationListener.Current;
                var access = listener.GetAccessStatus();
                if (access != UserNotificationListenerAccessStatus.Allowed)
                {
                    inbox.Reset();
                    Status = access == UserNotificationListenerAccessStatus.Denied ? "Access denied · allow DeskPill in Windows notification access settings" : "Notification access needs permission";
                    return new List<DesktopNotification>();
                }
                IReadOnlyList<UserNotification> native;
                using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    native = await listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask(deadline.Token);
                if (disposed) return new List<DesktopNotification>();
                var nativeByKey = new Dictionary<string, UserNotification>();
                var identities = new List<DesktopNotification>();
                foreach (var item in native)
                {
                    string key = item.Id + ":" + item.CreationTime.UtcDateTime.Ticks;
                    nativeByKey[key] = item;
                    identities.Add(new DesktopNotification { Key = key, CreatedAt = item.CreationTime });
                }
                // Resolve text/app metadata only for new arrivals, keeping each routine poll small.
                var arrivals = inbox.Observe(identities, DateTimeOffset.UtcNow);
                var projected = new List<DesktopNotification>();
                foreach (var arrival in arrivals)
                {
                    try
                    {
                        UserNotification item = nativeByKey[arrival.Key];
                        string appId = "", appName = "Notification";
                        try { appId = item.AppInfo.AppUserModelId ?? ""; appName = Limit(item.AppInfo.DisplayInfo.DisplayName, 80); }
                        catch { /* Some desktop toasts have no resolvable app display metadata. */ }
                        if (appId.IndexOf("DeskPill", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        var binding = item.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric) ?? item.Notification.Visual.Bindings.FirstOrDefault();
                        if (binding == null) continue;
                        var text = binding.GetTextElements().Select(t => t.Text ?? "").ToArray();
                        if (text.Length == 0) continue;
                        projected.Add(new DesktopNotification
                        {
                            Key = arrival.Key, AppId = appId, AppName = appName,
                            Title = Limit(text[0], 160), Body = Limit(string.Join("\n", text.Skip(1)), 600),
                            CreatedAt = item.CreationTime
                        });
                    }
                    catch { /* A toast removed during projection must not stop other arrivals. */ }
                }
                Status = "Listening for new Windows notifications";
                return projected;
            }
            catch (Exception ex)
            {
                Status = "Notification listener unavailable (0x" + ex.HResult.ToString("X8") + ")";
                return new List<DesktopNotification>();
            }
            finally { Interlocked.Exchange(ref polling, 0); }
        }

        public async Task<string> RequestAccessAsync()
        {
            try
            {
                var access = await UserNotificationListener.Current.RequestAccessAsync();
                Status = access == UserNotificationListenerAccessStatus.Allowed ? "Notification access is allowed" : "Access " + access + " · allow notification access in Windows Settings";
            }
            catch (Exception ex) { Status = "Notification access unavailable (0x" + ex.HResult.ToString("X8") + ")"; }
            return Status;
        }
        static string Limit(string value, int max) { value = value ?? ""; return value.Length <= max ? value : value.Substring(0, max); }
        public void Dispose() { disposed = true; inbox.Reset(); }
    }
}
