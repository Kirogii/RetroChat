using System.Drawing;
using System.Windows.Forms;

namespace RobloxChatLauncher.UI
{
    internal sealed class HelpForm : Form
    {
        public HelpForm()
        {
            Text = "Roblox Chat Launcher - Commands";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            ShowInTaskbar = true;
            MinimizeBox = true;
            MaximizeBox = true;
            MinimumSize = new Size(620, 420);
            ClientSize = new Size(820, 620);
            BackColor = Color.White;

            var browser = new WebBrowser
            {
                Dock = DockStyle.Fill,
                AllowNavigation = false,
                AllowWebBrowserDrop = false,
                IsWebBrowserContextMenuEnabled = false,
                ScriptErrorsSuppressed = true,
                WebBrowserShortcutsEnabled = true,
                DocumentText = BuildHtml()
            };
            Controls.Add(browser);
        }

        private static string BuildHtml() => """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Roblox Chat Launcher Commands</title>
<style>
  html, body { margin: 0; padding: 0; background: #f4f5f7; color: #24292f; font-family: "Segoe UI", Arial, sans-serif; }
  body { padding: 28px; }
  main { max-width: 920px; margin: 0 auto; background: white; border: 1px solid #d8dee4; border-radius: 8px; overflow: hidden; }
  header { padding: 22px 26px; background: #20252b; color: white; }
  h1 { margin: 0 0 5px; font-size: 24px; font-weight: 600; }
  header p { margin: 0; color: #c9d1d9; }
  table { width: 100%; border-collapse: collapse; }
  th { position: sticky; top: 0; background: #eef1f4; text-align: left; padding: 12px 14px; border-bottom: 1px solid #d8dee4; }
  td { padding: 12px 14px; vertical-align: top; border-bottom: 1px solid #e8ebee; line-height: 1.45; }
  tr:last-child td { border-bottom: 0; }
  code { white-space: nowrap; padding: 2px 6px; color: #b42318; background: #fff1f0; border-radius: 4px; font-family: Consolas, monospace; }
  .aliases { color: #57606a; }
</style>
</head>
<body>
<main>
  <header><h1>Chat commands</h1><p>Type a command in the in-game chat input and press Enter.</p></header>
  <table>
    <thead><tr><th>Command</th><th>Aliases</th><th>Description</th></tr></thead>
    <tbody>
      <tr><td><code>/help</code></td><td class="aliases"><code>/?</code></td><td>Open this command window.</td></tr>
      <tr><td><code>/about</code></td><td class="aliases"><code>/credits</code></td><td>Show application information and credits.</td></tr>
      <tr><td><code>/reconnect</code></td><td class="aliases"><code>/rc</code></td><td>Reconnect to the configured chat server.</td></tr>
      <tr><td><code>/echo &lt;text&gt;</code></td><td></td><td>Test the server's HTTP message endpoint.</td></tr>
      <tr><td><code>/clear</code></td><td class="aliases"><code>/cls</code> <code>/c</code></td><td>Clear the visible chat history.</td></tr>
      <tr><td><code>/id</code></td><td class="aliases"><code>/channel</code></td><td>Show the current Roblox server/channel ID.</td></tr>
      <tr><td><code>/channel list</code></td><td></td><td>Open the live channel list and join another chatroom.</td></tr>
      <tr><td><code>/bug</code></td><td class="aliases"><code>/issue</code></td><td>Open the GitHub issue page.</td></tr>
      <tr><td><code>/mute &lt;name&gt;</code></td><td></td><td>Hide messages from a user.</td></tr>
      <tr><td><code>/unmute &lt;name&gt;</code></td><td></td><td>Show messages from a muted user again.</td></tr>
      <tr><td><code>/whisper &lt;name&gt; &lt;text&gt;</code></td><td class="aliases"><code>/w</code></td><td>Send a private message to a user in the channel.</td></tr>
      <tr><td><code>/filter &lt;mode&gt;</code></td><td></td><td>Set <code>strict</code>, <code>default</code>, <code>relaxed</code>, or <code>off</code>.</td></tr>
      <tr><td><code>/coregui 2013</code></td><td></td><td>Show the 2013 player list and Escape menu overlay.</td></tr>
      <tr><td><code>/coregui 2016</code></td><td></td><td>Show the 2016 top bar, player list, and settings hub. Use <code>/coregui off</code> to disable.</td></tr>
      <tr><td><code>/cookie &lt;value&gt;</code></td><td></td><td>Store Roblox authentication encrypted on this Windows account only. Never sent to the chat server. <code>/cookie clear</code> deletes it. Friend requests require a verified target and confirmation.</td></tr>
      <tr><td><code>/radmin &lt;ip[:port]&gt;</code></td><td></td><td>Save a new chat server and reconnect. Default port: 10000.</td></tr>
      <tr><td><code>/theme &lt;year&gt;</code></td><td></td><td>Use <code>default</code>, <code>2014</code>, <code>2016</code>, or <code>2018</code>.</td></tr>
      <tr><td><code>/drag on|off</code></td><td></td><td>Allow or lock dragging. Drag the historical chat history or input; in the default theme, hold and drag the chat button. Positions and the setting are remembered.</td></tr>
      <tr><td><code>/theme 2014 [classic|docked]</code></td><td></td><td>Defaults to the original in-game bottom bar with translucent history. Use <code>/theme 2014 docked</code> to fit Roblox above a taller bottom bar, above the Windows taskbar.</td></tr>
      <tr><td><code>/text dark|light</code></td><td></td><td>Choose dark or light message text.</td></tr>
      <tr><td><code>:sku</code></td><td></td><td>Search emoji by shortcode. Choose with Up/Down and Tab/Enter, or click a suggestion. Escape dismisses suggestions.</td></tr>
      <tr><td>Window controls</td><td></td><td>Click the round chat button to hide/show chat; hold and drag it with <code>/drag on</code>. Drag the lower-right history grip to resize historical themes.</td></tr>
      <tr><td><code>/console</code></td><td class="aliases"><code>/debug</code></td><td>Open or close the diagnostic console.</td></tr>
      <tr><td><code>/update</code></td><td></td><td>Check for an update. Add <code>prerelease</code> to include prereleases.</td></tr>
      <tr><td><code>/login</code></td><td></td><td>Log in using an account previously linked to this computer.</td></tr>
      <tr><td><code>/logout</code></td><td></td><td>Clear local login state.</td></tr>
      <tr><td><code>/verify &lt;username&gt;</code></td><td></td><td>Begin Roblox account verification.</td></tr>
      <tr><td><code>/confirm</code></td><td></td><td>Complete a pending account verification.</td></tr>
      <tr><td><code>/unverify</code></td><td></td><td>Remove the linked Roblox account.</td></tr>
      <tr><td><code>/emote &lt;name&gt;</code></td><td class="aliases"><code>/e</code></td><td>Request a supported integrated-game emote.</td></tr>
    </tbody>
  </table>
</main>
</body>
</html>
""";
    }
}
