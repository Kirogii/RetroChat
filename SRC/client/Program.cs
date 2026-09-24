using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using RobloxChatLauncher.Core;
using RobloxChatLauncher.Services;
using RobloxChatLauncher.Utils;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if(args.Length==2&&args[0]=="--ocr-settings-preview")
        {
            using var image=new Bitmap(args[1]);
            var lines=RobloxSettingsOcr.ReadSettingsAsync(image).GetAwaiter().GetResult();
            foreach(var line in lines)Console.WriteLine($"{line.Bounds.Y,4} {line.Bounds.X,4} {line.Text}");
            foreach(string name in RobloxSettingsAutomation.NativeLabels)
            {
                var row=RobloxSettingsAutomation.ReadRow(image,lines,name);
                if(row!=null)
                {
                    Console.WriteLine($"SETTING {name}: {row.Value??"?"} [{row.Segments.Length} bars; selected={RobloxSettingsAutomation.IsRowSelected(image,row)}]");
                    if(row.Segments.Length>0)Console.WriteLine(string.Join("; ",row.Segments.Select(bar=>$"{bar.X},{bar.Y},{bar.Width}:{image.GetPixel(bar.X+bar.Width/2,bar.Y).R}")));
                }
            }
            return;
        }
        if(args.Length==2&&args[0]=="--verify-frame-rate-popup")
        {
            try { RobloxChatLauncher.UI.RobloxSettingsReferenceChecks.CheckFrameRatePopupAsync(args[1]).GetAwaiter().GetResult(); }
            catch(Exception ex) { Console.Error.WriteLine(ex.Message);Environment.ExitCode=1; }
            return;
        }
        if(args.Length==2&&args[0]=="--verify-settings-references")
        {
            try { RobloxChatLauncher.UI.RobloxSettingsReferenceChecks.RunAsync(args[1]).GetAwaiter().GetResult(); }
            catch(Exception ex) { Console.Error.WriteLine(ex.Message);Environment.ExitCode=1; }
            return;
        }
        if (args.Length == 2 && args[0] == "--render-theme-previews")
        {
            try { RobloxChatLauncher.UI.CoreGuiPreview.Run(args[1]); }
            catch(Exception ex) { Console.Error.WriteLine(ex.Message);Environment.ExitCode=1; }
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        bool Has(string arg) => args.Contains(arg, StringComparer.OrdinalIgnoreCase);
        if (Has("--uninstall")) { RobloxRegistryUtil.Restore(); return; }
        if (Has("--register-only")) { RobloxRegistryUtil.Register(); return; }
        bool watch = Has("--watch") || Has("--force-run");
        if (!watch && !ServerEndpoint.EnsureConfigured()) return;
        if (!watch) RobloxRegistryUtil.Register();

        // Forward every game URI even if the watcher is already running.
        // A second invocation must never terminate the existing overlay.
        string? uri = args.FirstOrDefault(a => a.StartsWith("roblox", StringComparison.OrdinalIgnoreCase));
        if (!watch && (uri != null || !Process.GetProcessesByName("RobloxPlayerBeta").Any()))
        {
            var client = RobloxLocator.ResolveRobloxPlayer();
            if (client == null) { MessageBox.Show("Roblox could not be found. Install or launch Roblox once, then try again."); return; }
            Process.Start(new ProcessStartInfo(client.ExecutablePath, uri ?? "") { UseShellExecute = true });
        }
        using var mutex = new Mutex(true, $"Local\\{Constants.APP_GUID}-watcher", out bool first);
        if (!first) return;
        try { Application.Run(new RobloxWatcher()); }
        finally { mutex.ReleaseMutex(); }
    }
}
