using System.Net;
using System.Drawing;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using RobloxChatLauncher.Core;

namespace RobloxChatLauncher.UI;

internal sealed class ChannelListForm : Form
{
    private readonly WebBrowser browser = new() { Dock=DockStyle.Fill,AllowWebBrowserDrop=false,
        IsWebBrowserContextMenuEnabled=false,ScriptErrorsSuppressed=true };
    internal event Action<string>? JoinRequested;
    internal ChannelListForm(string current)
    {
        Text="Roblox Chat Launcher - Channels";StartPosition=FormStartPosition.CenterScreen;
        MinimumSize=new Size(520,380);ClientSize=new Size(680,540);Controls.Add(browser);
        browser.Navigating+=(_,e)=> { if(e.Url.Host.Equals("join.local",StringComparison.OrdinalIgnoreCase)) {
            e.Cancel=true; JoinRequested?.Invoke(Uri.UnescapeDataString(e.Url.Query.TrimStart('?'))); } };
        Shown+=async (_,_)=>await RefreshAsync(current);
    }
    private async Task RefreshAsync(string current)
    {
        browser.DocumentText=Build(current,"<li class='state'><strong>Loading channels…</strong></li>");
        try
        {
            string json=await ChatForm.Client.GetStringAsync(ServerEndpoint.HttpBaseUrl+"/api/v1/channels");
            var channels=(JsonNode.Parse(json)?["channels"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new();
            string rows=channels.Count==0 ? "<li class='state'><strong>No channels are available</strong></li>" :
                string.Join("",channels.Select(c=>{
                    string id=c["id"]?.ToString()??"";int users=c["users"]?.GetValue<int>()??0;
                    return $"<li class='row'><span class='dot'></span><span class='info'><b>{WebUtility.HtmlEncode(id)}</b><small>{users} connected user{(users==1?"":"s")}</small></span><a href='https://join.local/?{Uri.EscapeDataString(id)}'>{(id==current?"Rejoin":"Join")}</a></li>";
                }));
            browser.DocumentText=Build(current,rows);
        }
        catch(Exception ex) { browser.DocumentText=Build(current,$"<li class='state'><strong>Could not load channels</strong>{WebUtility.HtmlEncode(ex.Message)}</li>"); }
    }
    private static string Build(string current,string rows) => $$"""
<!doctype html><html><head><meta charset="utf-8"><style>
*{box-sizing:border-box}html,body{margin:0;height:100%;background:#101820;color:#e7eef3;font:13px "Segoe UI",Arial,sans-serif}header{padding:19px 20px 15px;background:#121d25;border-bottom:1px solid #2c3d48}h1{margin:0 0 5px;font-size:20px}.sub,small{color:#9eb0bc}.current{padding:12px 20px;background:#14212a;border-bottom:1px solid #22333d}.current span{color:#9eb0bc;text-transform:uppercase;font-size:11px;margin-right:10px}ul{list-style:none;margin:12px;padding:0;border:1px solid #2c3d48;background:#182630}.row{min-height:54px;padding:8px 9px 8px 13px;border-bottom:1px solid #22333d;display:flex;align-items:center}.dot{width:7px;height:7px;border-radius:50%;background:#69d391;margin-right:11px}.info{min-width:0;flex:1}.info b,.info small{display:block;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.row a{padding:7px 14px;background:#2c91d3;border:1px solid #48a9e7;color:white;text-decoration:none;font-weight:600}.state{padding:34px 22px;text-align:center;color:#9eb0bc}.state strong{display:block;color:#e7eef3;margin-bottom:4px}
</style></head><body><header><h1>Chat Channels</h1><div class="sub">Choose a chatroom to join. Your Roblox game stays open.</div></header><div class="current"><span>Current</span>{{WebUtility.HtmlEncode(current)}}</div><ul>{{rows}}</ul></body></html>
""";
}
