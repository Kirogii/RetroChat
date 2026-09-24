using Microsoft.Win32;

namespace RobloxChatLauncher.Utils
{
    // Helps differentiate between the vanilla Roblox Client and bootstrappers
    // public enum my beloved
    public enum RobloxClientType
    {
        Vanilla,
        Bootstrapper
    }

    public record RobloxClientInfo(string ExecutablePath, RobloxClientType Type, string Name);

    public static class RobloxLocator
    {
        // Resolves either a bootstrapper Client or the vanilla Roblox Client
        public static RobloxClientInfo ResolveRobloxPlayer()
        {
            // List of known bootstrappers to check for in the registry. The first one found will be used.
            string[] bootstrappers = { "Bloxstrap", "Voidstrap", "Fishstrap", "Froststrap", "Funkstrap" };

            foreach (string bootstrapper in bootstrappers)
            {
                // Check the Uninstall key for the bootstrapper's icon path. If it exists, we assume it's a valid Roblox Client and return it.
                using var key = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{bootstrapper}");
                string rawPath = key?.GetValue("DisplayIcon") as string;

                if (!string.IsNullOrEmpty(rawPath))
                {
                    // Split the comma to remove the icon index and trim quotes if they exist
                    string exePath = rawPath.Split(',')[0].Trim('\"');
                    if (File.Exists(exePath))
                        return new RobloxClientInfo(exePath, RobloxClientType.Bootstrapper, bootstrapper);
                }
            }

            // Else return the vanilla RobloxPlayerBeta.exe
            string vanillaPath = ResolveVanillaRobloxPlayerPath();
            if (vanillaPath != null)
                return new RobloxClientInfo(vanillaPath, RobloxClientType.Vanilla, "Vanilla");

            return null;
        }

        public static string GetVanillaRobloxVersionFolder()
        {
            // This key stores the version folder name of the currently installed Roblox, which is used to construct the path to RobloxPlayerBeta.exe
            using var key = Registry.ClassesRoot.OpenSubKey(@"roblox-player\shell\open\command");
            return key?.GetValue("version") as string;
        }

        public static string ResolveVanillaRobloxPlayerPath()
        {
            var versionFolder = GetVanillaRobloxVersionFolder();
            if (string.IsNullOrEmpty(versionFolder))
            {
                string versions = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions");
                if (!Directory.Exists(versions)) return null;
                return new DirectoryInfo(versions).EnumerateDirectories("version-*")
                    .OrderByDescending(d => d.LastWriteTimeUtc)
                    .Select(d => Path.Combine(d.FullName, "RobloxPlayerBeta.exe")).FirstOrDefault(File.Exists);
            }

            // Construct the path to RobloxPlayerBeta.exe based on the version folder
            var exePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox", "Versions", versionFolder, "RobloxPlayerBeta.exe");

            return File.Exists(exePath) ? exePath : null;
        }

        public static string? FindContentFile(string relativePath)
        {
            // Portable installs may not have a version value in the protocol key.
            string? player = ResolveVanillaRobloxPlayerPath();
            if (player != null)
            {
                string path = Path.Combine(Path.GetDirectoryName(player)!, "content", relativePath);
                if (File.Exists(path)) return path;
            }
            string versions = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions");
            if (!Directory.Exists(versions)) return null;
            foreach (var directory in new DirectoryInfo(versions).EnumerateDirectories("version-*").OrderByDescending(d => d.LastWriteTimeUtc))
            {
                string path = Path.Combine(directory.FullName, "content", relativePath);
                if (File.Exists(path)) return path;
            }
            return null;
        }
    }
}
