using System.Net;
using System.Windows.Forms;

namespace RobloxChatLauncher.Core
{
    internal static class ServerEndpoint
    {
        private const int DefaultRadminPort = 10000;

        internal static bool TrySetRadminAddress(string value, out string error)
        {
            error = "Usage: /radmin <IPv4 address[:port]>";
            string text = value.Trim();
            string[] parts = text.Split(':');
            if (parts.Length > 2 || parts[0].Split('.').Length != 4 ||
                !IPAddress.TryParse(parts[0], out var address) ||
                address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
                (parts.Length == 2 && (!int.TryParse(parts[1], out int port) || port < 1 || port > 65535))) return false;
            if (!TryNormalize(text, out string normalized, out error)) return false;
            Properties.Settings1.Default.ServerAddress = normalized;
            Properties.Settings1.Default.Save();
            return true;
        }

        public static string HttpBaseUrl => BuildBaseUri(webSocket: false).ToString().TrimEnd('/');
        public static string WebSocketBaseUrl => BuildBaseUri(webSocket: true).ToString().TrimEnd('/');

        public static bool EnsureConfigured()
        {
            if (!string.IsNullOrWhiteSpace(Properties.Settings1.Default.ServerAddress))
                return true;

            using var form = new Form
            {
                Text = "Roblox Chat Launcher server",
                Width = 470,
                Height = 190,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                MaximizeBox = false,
                MinimizeBox = false
            };
            var label = new Label
            {
                Left = 18,
                Top = 18,
                Width = 420,
                Height = 42,
                Text = "Enter the Radmin VPN IP of the computer hosting the chat server.\nThe default server port is 10000."
            };
            var input = new TextBox { Left = 18, Top = 68, Width = 420, Text = "26." };
            var save = new Button { Text = "Save", Left = 278, Top = 105, Width = 75, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 363, Top = 105, Width = 75, DialogResult = DialogResult.Cancel };
            form.Controls.AddRange([label, input, save, cancel]);
            form.AcceptButton = save;
            form.CancelButton = cancel;

            while (form.ShowDialog() == DialogResult.OK)
            {
                if (TryNormalize(input.Text, out string normalized, out string error))
                {
                    Properties.Settings1.Default.ServerAddress = normalized;
                    Properties.Settings1.Default.Save();
                    return true;
                }
                MessageBox.Show(error, "Invalid server address", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return false;
        }

        private static Uri BuildBaseUri(bool webSocket)
        {
            string configured = Properties.Settings1.Default.ServerAddress;
            if (!TryNormalize(configured, out string normalized, out _))
                normalized = "https://RobloxChatLauncher.onrender.com";

            var source = new Uri(normalized);
            string scheme = webSocket
                ? (source.Scheme == Uri.UriSchemeHttps ? "wss" : "ws")
                : source.Scheme;
            return new UriBuilder(source) { Scheme = scheme }.Uri;
        }

        private static bool TryNormalize(string value, out string normalized, out string error)
        {
            normalized = string.Empty;
            error = "Enter a Radmin IPv4 address, optionally followed by :port (for example 26.10.20.30:10000).";
            string text = (value ?? string.Empty).Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(text)) return false;

            if (!text.Contains("://", StringComparison.Ordinal))
                text = "http://" + text;
            if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
            if (string.IsNullOrWhiteSpace(uri.Host)) return false;

            int port = uri.IsDefaultPort ? DefaultRadminPort : uri.Port;
            if (port is < 1 or > 65535) return false;
            normalized = new UriBuilder(uri.Scheme, uri.Host, port).Uri.ToString().TrimEnd('/');
            error = string.Empty;
            return true;
        }
    }
}
