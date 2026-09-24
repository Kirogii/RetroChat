using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using RobloxChatLauncher.Core;

namespace RobloxChatLauncher.Services;

internal sealed class RobloxWatcher : ApplicationContext
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 750 };
    private readonly NotifyIcon tray;
    private readonly Dictionary<int, DateTime> candidates = new();
    private ChatForm? chat;
    private ChatKeyboardHandler? keyboard;
    private DateTime retryAt;
    private int attachedId;

    public RobloxWatcher()
    {
        tray = new NotifyIcon { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
            Text = "Roblox Chat — waiting for Roblox", Visible = true };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Exit chat launcher", null, (_, _) => ExitThread());
        tray.ContextMenuStrip = menu;
        timer.Tick += (_, _) => Poll();
        timer.Start();
        Poll();
    }
    private void Poll()
    {
        Utils.NativeMethods.GetWindowThreadProcessId(Utils.NativeMethods.GetForegroundWindow(), out uint foreground);
        if (chat != null)
        {
            if (foreground != 0 && foreground != attachedId)
            {
                try
                {
                    using var focused = Process.GetProcessById((int)foreground);
                    if (focused.ProcessName == "RobloxPlayerBeta") chat.Close();
                }
                catch (ArgumentException) { }
            }
            if (chat != null) return;
        }
        if (DateTime.UtcNow < retryAt) return;
        var live = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName("RobloxPlayerBeta").OrderByDescending(p => p.Id == foreground).ThenByDescending(p => p.Id))
        {
            bool retained = false;
            try
            {
                live.Add(process.Id);
                if (process.HasExited || process.MainWindowHandle == IntPtr.Zero) { candidates.Remove(process.Id); continue; }
                if (!candidates.TryGetValue(process.Id, out var firstSeen)) { candidates[process.Id] = DateTime.UtcNow; continue; }
                if ((DateTime.UtcNow - firstSeen).TotalSeconds < 2) continue;
                if (!ServerEndpoint.EnsureConfigured()) { retryAt = DateTime.UtcNow.AddSeconds(30); return; }
                chat = new ChatForm(process, true);
                attachedId = process.Id;
                keyboard = new ChatKeyboardHandler(chat);
                chat.AttachKeyboardHandler(keyboard);
                retained = true;
                chat.FormClosed += (_, _) =>
                {
                    keyboard?.Dispose(); keyboard = null;
                    chat = null;
                    process.Dispose();
                    candidates.Clear();
                    tray.Text = "Roblox Chat — waiting for Roblox";
                };
                chat.Show();
                if (process.HasExited) { chat.Close(); return; }
                tray.Text = "Roblox Chat — connected to Roblox";
                return;
            }
            catch (Exception ex)
            {
                chat?.Dispose(); chat = null;
                keyboard?.Dispose(); keyboard = null;
                retryAt = DateTime.UtcNow.AddSeconds(10);
                Debug.WriteLine(ex);
                tray.Text = "Roblox Chat — retrying attachment";
            }
            finally { if (!retained) process.Dispose(); }
        }
        foreach (var stale in candidates.Keys.Where(id => !live.Contains(id)).ToArray()) candidates.Remove(stale);
    }
    protected override void ExitThreadCore()
    {
        timer.Stop(); timer.Dispose();
        chat?.Close(); keyboard?.Dispose();
        tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose();
        base.ExitThreadCore();
    }
}
