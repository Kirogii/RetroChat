using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using RobloxChatLauncher.UI;
using RobloxChatLauncher.Utils;
using RobloxChatLauncher.Services;

namespace RobloxChatLauncher;
public partial class ChatForm
{
    private CoreGui2013? classicGui;
    private CoreGui2016? coreGui2016;
    private List<ChatMember> roomMembers = new();
    private string? localRosterMemberId;
    private bool forwardingNativeMenu;
    private bool settingsAutomationActive;
    private RobloxSettingsResult? loadedNativeSettings;

    private bool nativeMenuOpen;
    private long nativeEscapeGuardUntil;
    private bool classicRecording;
    private long lastHealthSampleAt;
    internal bool ForwardingNativeInput=>forwardingNativeMenu;
    internal void AttachKeyboardHandler(ChatKeyboardHandler handler)=>keyboardHandler=handler;
    private Form? ActiveCoreGuiForm=>(Form?)coreGui2016??classicGui;
    internal bool ClassicGuiFocused => ActiveCoreGuiForm is Form gui && NativeMethods.GetForegroundWindow() == gui.Handle;
    private bool HandleCoreGui(string args)
    {
        string era=args.Trim().ToLowerInvariant();
        if (era == "off") { classicGui?.Dispose();classicGui=null;coreGui2016?.Dispose();coreGui2016=null;if(coreGuiWindow!=null)coreGuiWindow.DragEnabled=Properties.Settings1.Default.ChatDragEnabled;toggleBtn.Visible=true;RefreshCoreGui();return true; }
        if (era is not ("2013" or "2016")) { RichChatBox.AppendSystemMessage(chatBox,"Usage: /coregui 2013 | /coregui 2016 | /coregui off"); return true; }
        if(era=="2013")
        {
            coreGui2016?.Dispose();coreGui2016=null;
            if(coreGuiWindow!=null)coreGuiWindow.DragEnabled=Properties.Settings1.Default.ChatDragEnabled;
            toggleBtn.Visible=true;
            if(classicGui==null){classicGui = new CoreGui2013 { Owner=this };classicGui.ActionRequested += async action => await RunClassicAction(action);}
        }
        else
        {
            classicGui?.Dispose();classicGui=null;
            if(coreGui2016==null){coreGui2016=new CoreGui2016 { Owner=this };coreGui2016.ActionRequested+=async action=>await RunClassicAction(action);if(loadedNativeSettings!=null)coreGui2016.ApplySettingsResult(loadedNativeSettings);}
            ApplyTheme("2016",false);
            if(coreGuiWindow!=null){coreGuiWindow.DragEnabled=false;coreGuiWindow.MessageOffset=Point.Empty;coreGuiWindow.InputOffset=Point.Empty;coreGuiWindow.FrameSize=Size.Empty;}
            toggleBtn.Visible=false;
        }
        classicGui?.SetMembers(roomMembers);coreGui2016?.SetLocalMemberId(localRosterMemberId);coreGui2016?.SetMembers(roomMembers);RefreshClassicGui();
        RichChatBox.AppendSystemMessage(chatBox,$"{era} CoreGui enabled. Press Escape for the menu and Tab for the player list.");
        if(era=="2016")BeginInvoke((Action)(async ()=>await RunClassicAction("settings-sync")));
        return true;
    }
    private void ReceiveRoster(JsonNode data)
    {
        if (data["channelId"]?.ToString() != channelId) return;
        roomMembers = (data["members"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Select(m=>new ChatMember(m["id"]?.ToString() ?? "",RosterName(m),m["robloxId"]?.ToString(),
                m["sessionSeconds"]?.GetValue<long>() ?? 0,m["totalSeconds"]?.GetValue<long>() ?? 0,
                m["timezoneOffsetMinutes"]?.GetValue<int>(),m["timezoneLabel"]?.ToString())).ToList();
        classicGui?.SetMembers(roomMembers);coreGui2016?.SetMembers(roomMembers);
    }
    private void ReceiveRosterIdentity(JsonNode data)
    {
        string? value=data["memberId"]?.ToString();
        localRosterMemberId=string.IsNullOrWhiteSpace(value)?null:value;
        coreGui2016?.SetLocalMemberId(localRosterMemberId);
    }
    private static string RosterName(JsonObject member)
    {
        foreach(string field in new[]{"name","username","senderName","displayName"}) {
            string? value=member[field]?.ToString();
            if(!string.IsNullOrWhiteSpace(value))return value.Trim();
        }
        string id=member["id"]?.ToString() ?? "";
        return string.IsNullOrWhiteSpace(id)?"Guest":"Guest "+id[..Math.Min(6,id.Length)];
    }
    private void RefreshClassicGui()
    {
        Form? gui=ActiveCoreGuiForm;if(gui==null || robloxProcess.HasExited) return;
        if(settingsAutomationActive){gui.Hide();return;}
        var window = robloxProcess.MainWindowHandle;
        if (window == IntPtr.Zero || NativeMethods.IsIconic(window)) { gui.Hide(); return; }
        bool active = IsRobloxForegroundProcess() || ClassicGuiFocused || NativeMethods.GetForegroundWindow() == Handle;
        if (!active) { gui.Hide(); return; }
        NativeMethods.GetClientRect(window,out var rect); Point origin=Point.Empty; NativeMethods.ClientToScreen(window,ref origin);
        gui.Bounds = new Rectangle(origin,new Size(rect.Right,rect.Bottom));gui.TopMost=true;if(!gui.Visible)gui.Show();
        if(gui is CoreGui2016 modern){modern.SetChatVisible(!isWindowHidden);if(Environment.TickCount64-lastHealthSampleAt>=1000){lastHealthSampleAt=Environment.TickCount64;TryUpdateCoreGuiHealth(modern,new Rectangle(origin,new Size(rect.Right,rect.Bottom)));}modern.Render();}
    }
    private static void TryUpdateCoreGuiHealth(CoreGui2016 gui,Rectangle client)
    {
        // Read only the native bar's lower pixels; our own overlay bar ends at y=30.
        if(client.Width<250||client.Height<70){gui.SetHealthFraction(1);return;}
        try
        {
            using var sample=new Bitmap(145,7,PixelFormat.Format24bppRgb);
            using(var graphics=Graphics.FromImage(sample))graphics.CopyFromScreen(client.Right-145,client.Top+31,0,0,sample.Size);
            gui.SetHealthFraction(ReadNativeHealthFraction(sample));
        }
        catch(System.ComponentModel.Win32Exception){gui.SetHealthFraction(1);}catch(ExternalException){gui.SetHealthFraction(1);}
    }
    internal static float ReadNativeHealthFraction(Bitmap sample)
    {
        int first=-1,last=-1,green=0;
        for(int x=0;x<sample.Width;x++)
        {
            Color c=sample.GetPixel(x,3),above=sample.GetPixel(x,1),below=sample.GetPixel(x,5);
            bool flat=Math.Abs(c.R-above.R)+Math.Abs(c.G-above.G)+Math.Abs(c.B-above.B)<18
                &&Math.Abs(c.R-below.R)+Math.Abs(c.G-below.G)+Math.Abs(c.B-below.B)<18;
            if(flat&&c.G>95&&c.G>c.R+35&&c.G>c.B+15)
            {
                if(first<0)first=x;
                last=x;green++;
            }
        }
        // Missing or irregular pixels are scenery, not evidence of low health.
        if(first<4||first>35||last-first+1>green+3||green<4)return 1;
        return Math.Clamp(green/110f,0,1);
    }
    internal bool HandleClassicKey(Keys key)
    {
        if (ActiveCoreGuiForm == null || forwardingNativeMenu || isChatting) return false;
        if (nativeMenuOpen)
        {
            if(key==Keys.Escape){nativeMenuOpen=false;nativeEscapeGuardUntil=Environment.TickCount64+900;}
            return false;
        }
        if(key==Keys.Escape&&Environment.TickCount64<nativeEscapeGuardUntil)return false;
        bool menuOpen=classicGui?.MenuOpen??coreGui2016?.MenuOpen??false;
        if (key == Keys.Tab) { if(menuOpen&&coreGui2016!=null)return coreGui2016.HandleMenuKey(key); if(!menuOpen){classicGui?.TogglePlayerListMaximized();coreGui2016?.TogglePlayerListMaximized();} return true; }
        if (key == Keys.Escape) { classicGui?.ToggleMenu();coreGui2016?.ToggleMenu();return true; }
        if(coreGui2016?.HandleMenuKey(key)==true)return true;
        return menuOpen;
    }
    private async Task RunClassicAction(string action)
    {
        if (action.StartsWith("friend:") && long.TryParse(action[7..],out long id) && id>0)
        {
            if (MessageBox.Show("Send a Roblox friend request to this player?","Friend request",MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            classicGui?.SetBusy(true);coreGui2016?.SetBusy(true);
            try { RichChatBox.AppendSystemMessage(chatBox,await LocalRobloxAccount.RequestFriend(id)); }
            catch { RichChatBox.AppendSystemMessage(chatBox,"Friend request failed. Your cookie has not been sent to the chat server."); }
            finally { classicGui?.SetBusy(false);coreGui2016?.SetBusy(false); }
            return;
        }
        if (action == "resume") return;
        if(action=="toggle-chat"){ToggleVisibility();coreGui2016?.SetChatVisible(!isWindowHidden);return;}
        if(action=="emotes")
        {
            try{await SendRobloxMenuSequence(Keys.Oemcomma);}
            catch(Exception ex){RichChatBox.AppendSystemMessage(chatBox,"Could not toggle Roblox emotes: "+ex.Message);}
            return;
        }
        if(action=="backpack")
        {
            try{await SendRobloxMenuSequence(Keys.Oemtilde);}catch(Exception ex){RichChatBox.AppendSystemMessage(chatBox,"Could not toggle the Roblox backpack: "+ex.Message);}return;
        }
        if (action == "help") { new HelpForm().Show(); return; }
        if (action == "leave-confirm")
        {
            try { await SendRobloxMenuSequence(Keys.Escape,Keys.L,Keys.Enter); }
            catch(Exception ex) { RichChatBox.AppendSystemMessage(chatBox,"Could not send the leave command to Roblox: "+ex.Message); }
            return;
        }
        if (action == "reset-confirm")
        {
            try { await SendRobloxMenuSequence(Keys.Escape,Keys.R,Keys.Enter); }
            catch(Exception ex) { RichChatBox.AppendSystemMessage(chatBox,"Could not send the reset command to Roblox: "+ex.Message); }
            return;
        }
        if(action=="screenshot")
        {
            forwardingNativeMenu=true;
            try {
                SetForegroundWindow(robloxProcess.MainWindowHandle);await Task.Delay(180);
                NativeMethods.GetClientRect(robloxProcess.MainWindowHandle,out var rect);Point origin=Point.Empty;NativeMethods.ClientToScreen(robloxProcess.MainWindowHandle,ref origin);
                if(rect.Right<1||rect.Bottom<1)throw new InvalidOperationException("Roblox has no drawable client area.");
                using var image=new Bitmap(rect.Right,rect.Bottom,PixelFormat.Format24bppRgb);
                using(var graphics=Graphics.FromImage(image))graphics.CopyFromScreen(origin,Point.Empty,image.Size);
                string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),"Roblox");Directory.CreateDirectory(folder);
                string path=Path.Combine(folder,$"RobloxScreenShot{DateTime.Now:yyyyMMdd_HHmmssfff}.png");image.Save(path,ImageFormat.Png);
                RichChatBox.AppendSystemMessage(chatBox,"Screenshot saved: "+path);
            } catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or ExternalException or InvalidOperationException) {
                RichChatBox.AppendSystemMessage(chatBox,"Screenshot failed: "+ex.Message);
            } finally { forwardingNativeMenu=false; }
            return;
        }
        if(action=="record")
        {
            forwardingNativeMenu=true;
            try {
                SetForegroundWindow(robloxProcess.MainWindowHandle);await Task.Delay(80);SendKeys.SendWait("{F12}");
                classicRecording=!classicRecording;classicGui?.SetRecording(classicRecording);coreGui2016?.SetRecording(classicRecording);
            } finally { forwardingNativeMenu=false; }
            return;
        }
        if(action=="settings-sync"||action.StartsWith("setting-step:",StringComparison.Ordinal)||action.StartsWith("setting-set:",StringComparison.Ordinal))
        {
            if(settingsAutomationActive)return;
            string? label=null;int direction=0;int? targetStep=null;
            if(action!="settings-sync")
            {
                int first=action.IndexOf(':'),last=action.LastIndexOf(':');
                if(last<=first)return;
                label=action[(first+1)..last];
                if(!RobloxSettingsAutomation.IsAllowed(label))return;
                if(action.StartsWith("setting-set:"))
                {
                    if(!int.TryParse(action[(last+1)..],out int target))return;
                    targetStep=target;
                }
                else direction=action[(last+1)..]=="right"?1:-1;
            }
            settingsAutomationActive=true;forwardingNativeMenu=true;
            if(keyboardHandler!=null)keyboardHandler.BlockMouseInput=true;
            coreGui2016?.SetBusy(true);
            coreGui2016?.SuspendForNativeSettings();
            coreGuiWindow?.Hide();toggleBtn.Hide();emojiPicker?.Hide();
            try
            {
                IntPtr window=robloxProcess.MainWindowHandle;
                FocusRobloxInput(window);
                await Task.Delay(150);
                using var mouseShield=new RobloxMouseShield(window);
                var result=await RobloxSettingsAutomation.RunAsync(window,key=>SendNativeKey((ushort)key),label,direction,targetStep);
                var remembered=loadedNativeSettings==null?new Dictionary<string,string>():new Dictionary<string,string>(loadedNativeSettings.Values);
                foreach(var pair in result.Values)remembered[pair.Key]=pair.Value;
                loadedNativeSettings=result with { Values=remembered };
                if(coreGui2016 is { IsDisposed:false } modern)modern.ApplySettingsResult(result);
                if(!result.Success)RichChatBox.AppendSystemMessage(chatBox,result.Message);
            }
            catch(Exception ex)
            {
                if(!IsDisposed)RichChatBox.AppendSystemMessage(chatBox,"Could not adjust Roblox Settings: "+ex.Message);
            }
            finally
            {
                if(keyboardHandler!=null)keyboardHandler.BlockMouseInput=false;
                forwardingNativeMenu=false;settingsAutomationActive=false;nativeMenuOpen=false;
                if(coreGui2016 is { IsDisposed:false } modern){modern.SetBusy(false);modern.ReopenSettings(label);}
                if(!IsDisposed){RefreshClassicGui();RefreshCoreGui();}
            }
            return;
        }

        // Native menus own game settings/report permissions. Open Roblox's own menu.
        RichChatBox.AppendSystemMessage(chatBox,"Opening Roblox controls for " + action + ".");
        try {
            await SendRobloxMenuSequence(Keys.Escape);
            nativeMenuOpen = action is "settings" or "report";
            if(action=="settings")await OpenNativeSettingsTab();
        }
        catch(Exception ex) { RichChatBox.AppendSystemMessage(chatBox,"Could not open Roblox controls: "+ex.Message); }
    }
    private async Task OpenNativeSettingsTab()
    {
        await Task.Delay(300);
        await SendRobloxMenuSequence(Keys.Tab);
    }
    private async Task SendRobloxMenuSequence(params Keys[] keys)
    {
        forwardingNativeMenu=true;
        try
        {
            IntPtr window=robloxProcess.MainWindowHandle;
            if(window==IntPtr.Zero||robloxProcess.HasExited)return;
            FocusRobloxInput(window);
            await Task.Delay(180);
            for(int index=0;index<keys.Length;index++)
            {
                SendNativeKey((ushort)keys[index]);
                // Roblox animates its native Escape menu before accepting shortcuts.
                await Task.Delay(index==0&&keys.Length>1?420:180);
            }
        }
        finally { forwardingNativeMenu=false; }
    }
    private static void SendNativeKey(ushort key)
    {
        ushort scan=(ushort)MapVirtualKey(key,0);
        uint extended=(Keys)key is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown or Keys.Insert or Keys.Delete?1u:0u;
        INPUT[] input=
        {
            new() { type=1, data=new INPUTUNION { keyboard=new KEYBDINPUT { scanCode=scan,flags=8|extended } } },
            new() { type=1, data=new INPUTUNION { keyboard=new KEYBDINPUT { scanCode=scan,flags=10|extended } } }
        };
        if(SendInput((uint)input.Length,input,Marshal.SizeOf<INPUT>())!=(uint)input.Length)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    private static void SendUnicodeCharacter(char character)
    {
        INPUT[] input=
        {
            new() { type=1,data=new INPUTUNION { keyboard=new KEYBDINPUT { scanCode=character,flags=4 } } },
            new() { type=1,data=new INPUTUNION { keyboard=new KEYBDINPUT { scanCode=character,flags=6 } } }
        };
        if(SendInput((uint)input.Length,input,Marshal.SizeOf<INPUT>())!=(uint)input.Length)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    private async Task SendRobloxChatCommand(string text)
    {
        forwardingNativeMenu=true;
        try
        {
            IntPtr window=robloxProcess.MainWindowHandle;
            if(window==IntPtr.Zero||robloxProcess.HasExited)throw new InvalidOperationException("Roblox is not open.");
            FocusRobloxInput(window);await Task.Delay(120);
            SendNativeKey((ushort)Keys.OemQuestion);await Task.Delay(180);
            foreach(char character in text){SendUnicodeCharacter(character);await Task.Delay(12);}
            await Task.Delay(80);SendNativeKey((ushort)Keys.Enter);
        }
        finally{forwardingNativeMenu=false;}
    }
    private async Task<bool> HandleRobloxEmote(string args)
    {
        string emote=string.Join(' ',args.Trim().ToLowerInvariant().Split(' ',StringSplitOptions.RemoveEmptyEntries));
        emote=emote switch { "dance1"=>"dance 1","dance2"=>"dance 2","dance3"=>"dance 3",_=>emote };
        if(emote is not ("dance 1" or "dance 2" or "dance 3" or "laugh"))
        {
            RichChatBox.AppendSystemMessage(chatBox,"Usage: /e dance 1 (or dance1) | /e dance 2 | /e dance 3 | /e laugh");return true;
        }
        try{await SendRobloxChatCommand("/e "+emote);}
        catch(Exception ex){RichChatBox.AppendSystemMessage(chatBox,"Could not send the emote to Roblox: "+ex.Message);}
        return true;
    }
    private static void FocusRobloxInput(IntPtr window)
    {
        IntPtr foreground=NativeMethods.GetForegroundWindow();
        uint current=GetCurrentThreadId();
        uint target=GetWindowThreadProcessId(window,out _);
        uint foregroundThread=foreground==IntPtr.Zero?0:GetWindowThreadProcessId(foreground,out _);
        bool targetAttached=target!=0&&target!=current&&AttachThreadInput(current,target,true);
        bool foregroundAttached=foregroundThread!=0&&foregroundThread!=current&&foregroundThread!=target&&AttachThreadInput(current,foregroundThread,true);
        try { SetForegroundWindow(window);SetFocus(window); }
        finally {
            if(foregroundAttached)AttachThreadInput(current,foregroundThread,false);
            if(targetAttached)AttachThreadInput(current,target,false);
        }
    }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr window);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from,uint to,bool attach);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code,uint mapType);
    [DllImport("user32.dll",SetLastError=true)] private static extern uint SendInput(uint count,INPUT[] inputs,int size);
    [DllImport("user32.dll")] private static extern bool ClipCursor(ref NativeMethods.RECT rect);
    [DllImport("user32.dll")] private static extern bool ClipCursor(IntPtr rect);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extraInfo);
    [StructLayout(LayoutKind.Explicit,Size=40)] private struct INPUT { [FieldOffset(0)]public uint type;[FieldOffset(8)]public INPUTUNION data; }
    [StructLayout(LayoutKind.Explicit,Size=32)] private struct INPUTUNION { [FieldOffset(0)]public KEYBDINPUT keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort virtualKey;public ushort scanCode;public uint flags;public uint time;public IntPtr extraInfo; }
}
