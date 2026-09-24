using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using RobloxChatLauncher.Utils;
using RobloxChatLauncher.Services;

namespace RobloxChatLauncher.UI;

// 2016 desktop CoreGui shell, ported from the ROHTML layout and Roblox Core-Scripts.
internal sealed class CoreGui2016 : Form
{
    private enum HubPage { Players,Settings,Report,Help,Record,Reset,Leave }
    private ChatMember[] members=Array.Empty<ChatMember>();
    private long membersReceivedAt=Environment.TickCount64;
    private bool menuOpen,rosterVisible=true,recording;
    private bool chatVisible=true;
    private bool busy,restoreChat;
    private string? localMemberId;
    private bool menuAnimating,animationOpening,backpackHighlighted,emotesHighlighted;
    private float menuOffset;
    private long animationStarted;
    private float animationFrom,animationTo;
    private readonly System.Windows.Forms.Timer animationClock=new(){Interval=16};
    private readonly System.Windows.Forms.Timer pageClock=new(){Interval=16};
    private bool pageAnimating;
    private HubPage pageFrom;
    private int pageDirection;
    private float pageProgress;
    private long pageAnimationStarted;
    private bool confirmPositive=true;
    private readonly CoreGuiAvatars avatars=new();
    private int avatarRevision;
    private int membersRevision,settingsRevision;
    private string? selectedMember;
    private int playerScroll,rosterScroll,settingsScroll;
    private int settingsViewportHeight;
    private int selectedSetting=-1;

    private Rectangle settingsViewport;
    private readonly Dictionary<string,Rectangle> settingTracks=new();
    private string? draggingSetting;
    private int draggingSettingValue;
    private readonly Dictionary<string,int> pendingSliders=new();
    private readonly System.Windows.Forms.Timer sliderDelay=new(){Interval=1000};
    private readonly Dictionary<string,string> settingValues=new(StringComparer.OrdinalIgnoreCase);

    private static readonly RobloxSetting[] SettingsItems=RobloxSettingsAutomation.Settings;
    private float healthFraction=1;
    private Rectangle playerViewport;
    private Rectangle scrollThumb;
    private bool draggingScroll;
    private int dragY,dragOffset;
    internal int PlayerScroll=>playerScroll;
    internal int RosterScroll=>rosterScroll;
    private HubPage page=HubPage.Players;
    private HubPage previousPage=HubPage.Players;
    private string? hoveredAction;
    private string lastRenderState="";
    private readonly System.Windows.Forms.Timer clock=new(){Interval=1000};
    private Rectangle settingsButton,chatButton,backpackButton,emotesButton,rosterBounds;
    private readonly List<(Rectangle Bounds,string Action)> hits=new();
    internal event Action<string>? ActionRequested;
    internal bool MenuOpen=>menuOpen;
    protected override bool ShowWithoutActivation=>true;
    protected override CreateParams CreateParams { get { var cp=base.CreateParams;cp.ExStyle|=0x80000|0x80|0x08000000;return cp; } }
    internal CoreGui2016()
    {
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;AutoScaleMode=AutoScaleMode.None;TopMost=true;
        clock.Tick+=(_,_)=>{if(Visible)Render();};clock.Start();
        animationClock.Tick+=(_,_)=>AdvanceMenuAnimation();
        pageClock.Tick+=(_,_)=>AdvancePageAnimation();
        sliderDelay.Tick+=(_,_)=>FlushPendingSlider();
        avatars.Changed+=AvatarChanged;
    }
    private void AvatarChanged()
    {
        if(IsDisposed||Disposing||!IsHandleCreated)return;
        if(InvokeRequired)
        {
            try { BeginInvoke((Action)AvatarChanged); }
            catch(InvalidOperationException) { }
            return;
        }
        avatarRevision++;
        if(Visible)Render();
    }
    internal void SetLocalMemberId(string? value){if(localMemberId==value)return;localMemberId=value;if(Visible)Render();}
    internal void SetMembers(IEnumerable<ChatMember> value)
    {
        long now=Environment.TickCount64;var previous=members.ToDictionary(m=>m.Id,StringComparer.Ordinal);
        members=value.Select(member=>previous.TryGetValue(member.Id,out var old)
            ? member with { SessionSeconds=Math.Max(member.SessionSeconds,old.SessionSeconds+Math.Max(0,(now-membersReceivedAt)/1000)) }:member)
            .OrderBy(m=>m.Name,StringComparer.OrdinalIgnoreCase).ToArray();membersReceivedAt=now;membersRevision++;if(Visible)Render();
    }
    internal void TogglePlayerListMaximized(){if(menuOpen)return;rosterVisible=!rosterVisible;Render();}
    internal void ToggleMenu(){if(menuOpen&&page is HubPage.Reset or HubPage.Leave){page=previousPage;Render();return;}SetMenuOpen(!menuOpen);}
    private void SetMenuOpen(bool value)
    {
        if(menuOpen==value&&!menuAnimating)return;
        menuOpen=value;
        if(value){page=HubPage.Players;selectedSetting=-1;restoreChat=chatVisible;if(restoreChat){chatVisible=false;ActionRequested?.Invoke("toggle-chat");}}
        else if(restoreChat){restoreChat=false;chatVisible=true;ActionRequested?.Invoke("toggle-chat");}
        StartMenuAnimation(value);
    }
    private void StartMenuAnimation(bool opening)
    {
        animationOpening=opening;menuAnimating=true;animationStarted=Environment.TickCount64;
        animationFrom=menuOffset;
        if(!animationClock.Enabled&&opening&&menuOffset==0)animationFrom=-Height-36;
        animationTo=opening?0:-Height-36;animationClock.Start();Render();
    }
    private void AdvanceMenuAnimation()
    {
        float duration=animationOpening?.5f:.4f;
        float t=Math.Clamp((Environment.TickCount64-animationStarted)/(duration*1000f),0,1);
        float eased=animationOpening?(t<.5f?8*t*t*t*t:1-MathF.Pow(-2*t+2,4)/2):t*t;
        menuOffset=animationFrom+(animationTo-animationFrom)*eased;Render();
        if(t>=1){menuOffset=animationTo;menuAnimating=false;animationClock.Stop();Render();}
    }
    private void SwitchPage(HubPage value)
    {
        if(page==value)return;
        selectedSetting=-1;
        pageFrom=page;pageDirection=(int)value>=(int)page?1:-1;page=value;
        pageProgress=0;pageAnimationStarted=Environment.TickCount64;pageAnimating=true;pageClock.Start();Render();

    }
    private void AdvancePageAnimation()
    {
        float t=Math.Clamp((Environment.TickCount64-pageAnimationStarted)/100f,0,1);
        pageProgress=1-(1-t)*(1-t);Render();
        if(t>=1){pageProgress=1;pageAnimating=false;pageClock.Stop();Render();}
    }
    internal void SetRecording(bool value){recording=value;if(Visible)Render();}
    internal void SetHealthFraction(float value){value=Math.Clamp(value,0,1);if(Math.Abs(value-healthFraction)<.015f)return;healthFraction=value;if(Visible)Render();}
    internal void SetSettingValue(string label,string outcome)
    {
        string prefix=label+": ";
        if(!outcome.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))return;
        settingValues[label]=outcome[prefix.Length..];settingsRevision++;if(Visible)Render();
    }
    internal void SetChatVisible(bool value){if(chatVisible==value)return;chatVisible=value;if(Visible)Render();}
    internal void SetBusy(bool value){busy=value;if(Visible)Render();}
    internal bool HandleMenuKey(Keys key)
    {
        if(!menuOpen)return false;
        if(busy)return true;
        if(page is HubPage.Reset or HubPage.Leave)
        {
            if(key is Keys.Left or Keys.Up){confirmPositive=true;Render();}
            else if(key is Keys.Right or Keys.Down){confirmPositive=false;Render();}
            else if(key==Keys.Enter)ActivateAction(confirmPositive?(page==HubPage.Reset?"reset-confirm":"leave-confirm"):"back");
            return true;
        }
        if(key==Keys.Tab)
        {
            HubPage[] tabs={HubPage.Players,HubPage.Settings,HubPage.Report,HubPage.Help,HubPage.Record};
            int index=Array.IndexOf(tabs,page);
            SwitchPage(tabs[(index+1)%tabs.Length]);
        }
        else if(key==Keys.R)ActivateAction("reset");
        else if(key==Keys.L)ActivateAction("leave");
        else if(page==HubPage.Settings&&key is Keys.Up or Keys.Down)
        {
            SelectSetting(selectedSetting<0?0:selectedSetting+(key==Keys.Down?1:-1));
        }
        else if(page==HubPage.Settings&&key is Keys.Left or Keys.Right)
        {
            if(selectedSetting<0)SelectSetting(0);
            {
                string name=SettingsItems[selectedSetting].Name;
                if(RobloxSettingsAutomation.IsAllowed(name))ActivateAction("setting-step:"+name+":"+(key==Keys.Right?"right":"left"));
            }
        }
        else if(key is Keys.Left or Keys.Right)
        {
            HubPage[] tabs={HubPage.Players,HubPage.Settings,HubPage.Report,HubPage.Help,HubPage.Record};
            int index=Array.IndexOf(tabs,page);
            if(index>=0)SwitchPage(tabs[Math.Clamp(index+(key==Keys.Right?1:-1),0,tabs.Length-1)]);
        }
        return true;
    }
    internal void ScrollList(bool players,int delta)
    {
        if(players)playerScroll=Math.Clamp(playerScroll+delta,0,Math.Max(0,20+members.Length*80-playerViewport.Height));
        else rosterScroll=Math.Clamp(rosterScroll+delta,0,Math.Max(0,members.Length*26-Math.Max(0,rosterBounds.Height-24)));
        Render();
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if(menuOpen&&page==HubPage.Players&&playerViewport.Contains(e.Location))ScrollList(true,-e.Delta/120*60);
        else if(menuOpen&&page==HubPage.Settings){settingsScroll=Math.Clamp(settingsScroll-e.Delta/120*66,0,Math.Max(0,SettingsItems.Length*50-settingsViewportHeight));Render();}
        else if(!menuOpen&&rosterVisible&&rosterBounds.Contains(e.Location))ScrollList(false,-e.Delta/120*78);
    }
    internal bool ContainsInteractiveScreenPoint(Point screen)
    {
        Point local=PointToClient(screen);
        return menuOpen||hits.Any(hit=>hit.Bounds.Contains(local));
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x21){m.Result=(IntPtr)3;return;}
        if(m.Msg==0x84)
        {
            long packed=m.LParam.ToInt64();Point point=PointToClient(new Point((short)packed,(short)(packed>>16)));
            m.Result=(IntPtr)(menuOpen||hits.Any(h=>h.Bounds.Contains(point))?1:-1);return;
        }
        base.WndProc(ref m);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);if(e.Button!=MouseButtons.Left)return;
        if(draggingScroll){draggingScroll=false;Capture=false;return;}
        if(draggingSetting!=null){string name=draggingSetting;int target=draggingSettingValue;draggingSetting=null;Capture=false;ActivateAction("setting-set:"+name+":"+target);return;}
        string? action=hits.LastOrDefault(h=>h.Bounds.Contains(e.Location)).Action;
        if(action==null)return;
        ActivateAction(action);
    }
    internal void ActivateAction(string action)
    {
        if(action=="roster-none"||action=="players-none"||action=="scroll"||busy)return;
        if(action=="menu"){ToggleMenu();return;}
        if(action=="chat"){if(!menuOpen)ActionRequested?.Invoke("toggle-chat");return;}
        if(action=="roster"){rosterVisible=!rosterVisible;Render();return;}
        if(action.StartsWith("tab:")){SwitchPage(Enum.Parse<HubPage>(action[4..],true));return;}
        if(action.StartsWith("setting-row:")){selectedSetting=Array.FindIndex(SettingsItems,item=>item.Name==action[12..]);Render();return;}
        if(action=="resume"){SetMenuOpen(false);return;}
        if(action=="reset"){previousPage=page;confirmPositive=true;SwitchPage(HubPage.Reset);return;}
        if(action=="leave"){previousPage=page;confirmPositive=true;SwitchPage(HubPage.Leave);return;}
        if(action.StartsWith("player:")){selectedMember=selectedMember==action[7..]?null:action[7..];Render();return;}
        if(action=="back"){SwitchPage(previousPage);return;}
        if(action.StartsWith("friend:")){ActionRequested?.Invoke(action);return;}
        if(action.StartsWith("setting-step:")||action.StartsWith("setting-set:")){QueueSettingAction(action);return;}

        if(action=="backpack"){backpackHighlighted=!backpackHighlighted;Render();ActionRequested?.Invoke(action);return;}
        if(action=="emotes"){emotesHighlighted=!emotesHighlighted;Render();ActionRequested?.Invoke(action);return;}
        SetMenuOpen(false);ActionRequested?.Invoke(action);
    }
    private void QueueSettingAction(string action)
    {
        int first=action.IndexOf(':'),last=action.LastIndexOf(':');
        string name=action[(first+1)..last];
        var setting=SettingsItems.FirstOrDefault(item=>item.Name==name);
        if(setting?.Slider!=true){ActionRequested?.Invoke(action);return;}
        int current=pendingSliders.TryGetValue(name,out int pending)?pending:
            int.TryParse(settingValues.GetValueOrDefault(name),out int observed)?observed:setting.Minimum;
        int target=action.StartsWith("setting-set:")&&int.TryParse(action[(last+1)..],out int chosen)
            ?chosen:current+(action[(last+1)..]=="left"?-1:1);
        pendingSliders[name]=Math.Clamp(target,setting.Minimum,setting.Maximum);
        sliderDelay.Stop();sliderDelay.Start();
        settingsRevision++;Render();
    }
    internal void FlushPendingSlider()
    {
        if(busy||draggingSetting!=null)return;
        if(pendingSliders.Count==0){sliderDelay.Stop();return;}
        var next=pendingSliders.First();
        pendingSliders.Remove(next.Key);
        if(pendingSliders.Count==0)sliderDelay.Stop();
        settingsRevision++;
        ActionRequested?.Invoke("setting-set:"+next.Key+":"+next.Value);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if(e.Button!=MouseButtons.Left||busy)return;
        string? action=hits.LastOrDefault(hit=>hit.Bounds.Contains(e.Location)).Action;
        if(action?.StartsWith("setting-set:")==true)
        {
            int separator=action.LastIndexOf(':');
            draggingSetting=action[12..separator];
            draggingSettingValue=int.Parse(action[(separator+1)..]);
            Capture=true;Render();return;
        }
        if(scrollThumb.Contains(e.Location))
        {
            draggingScroll=true;dragY=e.Y;
            dragOffset=menuOpen?(page==HubPage.Settings?settingsScroll:playerScroll):rosterScroll;
            Capture=true;
        }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if(draggingSetting!=null&&settingTracks.TryGetValue(draggingSetting,out var track))
        {
            var setting=SettingsItems.First(item=>item.Name==draggingSetting);
            draggingSettingValue=Math.Clamp((int)Math.Ceiling((e.X-track.X)*setting.Maximum/(double)track.Width),setting.Minimum,setting.Maximum);
            Render();return;
        }
        if(draggingScroll)
        {
            var viewport=menuOpen?(page==HubPage.Settings?settingsViewport:playerViewport):new Rectangle(rosterBounds.X,rosterBounds.Y+24,rosterBounds.Width,Math.Max(0,rosterBounds.Height-24));
            int content=menuOpen?(page==HubPage.Settings?SettingsItems.Length*50:20+members.Length*80):members.Length*26;
            int target=dragOffset+(int)((long)(e.Y-dragY)*Math.Max(0,content-viewport.Height)/Math.Max(1,viewport.Height-scrollThumb.Height));
            if(menuOpen&&page==HubPage.Settings){settingsScroll=Math.Clamp(target,0,Math.Max(0,content-viewport.Height));Render();}
            else ScrollList(menuOpen,target-(menuOpen?playerScroll:rosterScroll));
            return;
        }
        string? next=hits.LastOrDefault(h=>h.Bounds.Contains(e.Location)).Action;
        if(page==HubPage.Settings&&next!=null&&next.StartsWith("setting-"))
        {
            int first=next.IndexOf(':'),last=next.LastIndexOf(':');
            string name=first==last?next[(first+1)..]:next[(first+1)..last];
            int index=Array.FindIndex(SettingsItems,item=>item.Name==name);
            if(index>=0)selectedSetting=index;
        }
        if(next!=hoveredAction){hoveredAction=next;Render();}
    }

    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hoveredAction=null;Render();}
    internal Bitmap CreateBitmap()
    {
        hits.Clear();scrollThumb=Rectangle.Empty;var bitmap=new Bitmap(Math.Max(1,Width),Math.Max(1,Height),PixelFormat.Format32bppPArgb);
        using var g=Graphics.FromImage(bitmap);g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        DrawTopbar(g);
        if(menuOpen||menuAnimating){var state=g.Save();g.TranslateTransform(0,menuOffset);DrawHub(g);g.Restore(state);}else if(rosterVisible)DrawRoster(g);
        return bitmap;
    }
    private void DrawTopbar(Graphics g)
    {
        using(var bg=new SolidBrush(Color.FromArgb(128,31,31,31)))g.FillRectangle(bg,0,0,Width,36);
        var shadow=CoreGui2016Textures.Get("dropshadow");if(shadow!=null)g.DrawImage(shadow,new Rectangle(0,36,Width,3));
        settingsButton=new Rectangle(0,0,50,36);chatButton=new Rectangle(50,0,50,36);backpackButton=new Rectangle(100,0,50,36);emotesButton=new Rectangle(150,0,50,36);
        hits.Add((settingsButton,"menu"));hits.Add((chatButton,"chat"));hits.Add((backpackButton,"backpack"));hits.Add((emotesButton,"emotes"));
        ImageCentered(g,"Hamburger",settingsButton);ImageCentered(g,chatVisible?"ChatDown":"Chat",chatButton);ImageCentered(g,backpackHighlighted?"BackpackDown":"Backpack",backpackButton);ImageCentered(g,emotesHighlighted?"EmotesIconDown":"EmotesIcon",emotesButton);
        using var bold=RichChatBox.GetCoreGuiFont(12,true);
        string name=localMemberId==null?"Player":members.FirstOrDefault(m=>m.Id==localMemberId)?.Name??"Player";
        DrawLabel(g,name,bold,new Rectangle(Math.Max(200,Width-170),1,156,22),Color.White,TextFormatFlags.Left|TextFormatFlags.Bottom|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
        int healthX=Math.Max(200,Width-163);using(var healthBack=new SolidBrush(Color.FromArgb(228,236,246)))g.FillRectangle(healthBack,healthX,27,156,3);
        Color healthColor=healthFraction>.5f?Color.FromArgb(27,252,107):healthFraction>.25f?Color.FromArgb(255,205,36):Color.FromArgb(245,65,65);
        using(var health=new SolidBrush(healthColor))g.FillRectangle(health,healthX,27,(int)Math.Round(156*healthFraction),3);
        hits.Add((new Rectangle(Math.Max(0,Width-170),0,170,36),"roster"));
    }
    private void DrawRoster(Graphics g)
    {
        int width=Math.Min(324,Math.Max(1,Width-2));int x=Width-width-2,y=38;
        int available=Math.Max(0,Height-y-26),contentHeight=Math.Min(members.Length*26,available);
        rosterScroll=Math.Clamp(rosterScroll,0,Math.Max(0,members.Length*26-contentHeight));
        rosterBounds=new Rectangle(x,y,width,24+contentHeight);hits.Add((rosterBounds,"roster-none"));
        using var font=RichChatBox.GetCoreGuiFont(12);using var bold=RichChatBox.GetCoreGuiFont(12,true);
        DrawRosterText(g,"Session",bold,new Rectangle(x+172,y,73,24),true);DrawRosterText(g,"Local Time",bold,new Rectangle(x+249,y,73,24),true);
        var viewport=new Rectangle(x,y+24,width,contentHeight);var state=g.Save();g.SetClip(viewport);
        for(int i=rosterScroll/26;i<members.Length;i++)
        {
            int rowY=y+24+i*26-rosterScroll;if(rowY>=viewport.Bottom)break;
            var member=members[i];
            using(var bg=new SolidBrush(member.Id==selectedMember?Color.FromArgb(190,106,106,106):Color.FromArgb(128,31,31,31))){g.FillRectangle(bg,x,rowY,170,24);g.FillRectangle(bg,x+172,rowY,75,24);g.FillRectangle(bg,x+249,rowY,75,24);}
            DrawAvatar(g,member,new Rectangle(x+3,rowY+3,18,18));
            DrawRosterText(g,member.Name,font,new Rectangle(x+23,rowY,147,24));
            long session=member.SessionSeconds+Math.Max(0,(Environment.TickCount64-membersReceivedAt)/1000);
            DrawRosterText(g,$"{session/60}:{session%60:00}",font,new Rectangle(x+172,rowY,73,24),true);
            string local=member.TimezoneOffsetMinutes is int offset?DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(offset)).ToString("h:mmtt"):"--:--";
            DrawRosterText(g,local,font,new Rectangle(x+249,rowY,73,24),true);
            hits.Add((Rectangle.Intersect(viewport,new Rectangle(x,rowY,width,24)),"player:"+member.Id));
        }
        g.Restore(state);DrawScrollThumb(g,viewport,members.Length*26,rosterScroll);
        var selected=members.FirstOrDefault(m=>m.Id==selectedMember);
        if(selected!=null)
        {
            int popupX=Math.Max(0,x-152),popupY=Math.Clamp(y+24+Array.IndexOf(members,selected)*26-rosterScroll,38,Math.Max(38,Height-172));
            var panel=new Rectangle(popupX,popupY,150,170);using var bg=new SolidBrush(Color.FromArgb(205,31,31,31));g.FillRectangle(bg,panel);hits.Add((panel,"roster-none"));
            DrawAvatar(g,selected,new Rectangle(popupX+39,popupY+4,72,72));DrawRosterText(g,selected.Name,bold,new Rectangle(popupX+4,popupY+78,142,22),true);
            if(long.TryParse(selected.RobloxId,out long id)&&id>0)DrawBottomButton(g,new Rectangle(popupX,popupY+104,150,32),"Send Friend Request","","friend:"+id);
            DrawBottomButton(g,new Rectangle(popupX,popupY+138,150,32),"Report Abuse","","report");
        }
    }
    private static void DrawRosterText(Graphics g,string text,Font font,Rectangle bounds,bool center=false)
    {
        DrawLabel(g,text,font,bounds,Color.FromArgb(255,255,243),(center?TextFormatFlags.HorizontalCenter:TextFormatFlags.Left)|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding|TextFormatFlags.PreserveGraphicsClipping);
    }
    private void DrawHub(Graphics g)
    {
        using(var shield=new SolidBrush(Color.FromArgb(204,41,41,41)))g.FillRectangle(shield,0,36,Width,Math.Max(0,Height-36));
        if(page is HubPage.Reset or HubPage.Leave){DrawHubPage(g,new Rectangle((Width-Math.Min(800,Width-20))/2,Math.Max(36,(Height-220)/2),Math.Min(800,Width-20),220),page);return;}
        const int hubWidth=800,barHeight=60;
        int largest=600,min=150;int buffer=(int)(Height*.05);int usable=Height-(buffer*2+barHeight*2);int pageHeight=Math.Clamp(usable,min,largest);
        int top=(largest<usable||usable<min)?(Height-pageHeight)/2-barHeight:buffer;int left=(Width-hubWidth)/2;int bottom=top+barHeight+pageHeight;
        DrawNineSlice(g,"MenuBackground",new Rectangle(left,top,hubWidth,barHeight),4,4,4,4);
        var tabs=new[]{
            (HubPage.Players,"Players","PlayersTabIcon",150,44,37,15),
            (HubPage.Settings,"Settings","GameSettingsTab",169,45,45,15),
            (HubPage.Report,"Report","ReportAbuseTab",150,36,43,20),
            (HubPage.Help,"Help","HelpTab",130,44,44,10),
            (HubPage.Record,"Record","RecordTab",130,41,40,5)};
        int tabSlot=hubWidth/tabs.Length;using var tabFont=RichChatBox.GetCoreGuiFont(20,true);
        for(int i=0;i<tabs.Length;i++)
        {
            int tabLeft=left+i*tabSlot+(tabSlot-tabs[i].Item4)/2;
            var rect=new Rectangle(tabLeft,top,tabs[i].Item4,barHeight);hits.Add((rect,"tab:"+tabs[i].Item1));
            int iconX=rect.X+tabs[i].Item7,iconY=rect.Y+(barHeight-tabs[i].Item6)/2;
            ImageAt(g,tabs[i].Item3,new Rectangle(iconX,iconY,tabs[i].Item5,tabs[i].Item6));
            Color color=page==tabs[i].Item1?Color.White:Color.FromArgb(128,255,255,255);
            int titleLeft=tabs[i].Item7+(int)Math.Round(tabs[i].Item5*1.2);
            DrawLabel(g,tabs[i].Item2,tabFont,new Rectangle(rect.X+titleLeft,rect.Y,rect.Width-titleLeft,rect.Height),color,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
            if(page==tabs[i].Item1)DrawNineSlice(g,"MenuSelection",new Rectangle(rect.X,rect.Bottom-6,rect.Width,6),3,1,3,1);
        }
        var pageBounds=new Rectangle(left,top+barHeight,hubWidth,pageHeight);
        if(pageAnimating)
        {
            var clip=g.Save();g.SetClip(pageBounds);int hitCount=hits.Count;
            var oldState=g.Save();g.TranslateTransform(-pageDirection*pageProgress*pageBounds.Width,0);DrawHubPage(g,pageBounds,pageFrom);g.Restore(oldState);hits.RemoveRange(hitCount,hits.Count-hitCount);
            var newState=g.Save();g.TranslateTransform(pageDirection*(1-pageProgress)*pageBounds.Width,0);DrawHubPage(g,pageBounds,page);g.Restore(newState);hits.RemoveRange(hitCount,hits.Count-hitCount);
            g.Restore(clip);
        }
        else DrawHubPage(g,pageBounds,page);
        DrawBottomButton(g,new Rectangle(left,bottom+5,260,70),"    Reset Character","ResetIcon","reset");
        DrawBottomButton(g,new Rectangle(left+270,bottom+5,260,70),"Leave Game","LeaveIcon","leave");
        DrawBottomButton(g,new Rectangle(left+540,bottom+5,260,70),"Resume Game","EscapeIcon","resume");
    }
    private void DrawHubPage(Graphics g,Rectangle bounds,HubPage shownPage)
    {
        using var title=RichChatBox.GetCoreGuiFont(28,true);using var text=RichChatBox.GetCoreGuiFont(18);
        string heading=shownPage switch { HubPage.Players=>"Players",HubPage.Settings=>"Settings",HubPage.Report=>"Report Abuse",HubPage.Help=>"Help",HubPage.Record=>recording?"Stop Recording":"Record",HubPage.Reset=>"Reset Character",_=>"Leave Game" };
        if(shownPage is not (HubPage.Players or HubPage.Settings or HubPage.Reset or HubPage.Leave))DrawLabel(g,heading,title,new Rectangle(bounds.X,bounds.Y+18,bounds.Width,42),Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
        if(shownPage==HubPage.Players)
        {
            playerViewport=bounds;playerScroll=Math.Clamp(playerScroll,0,Math.Max(0,20+members.Length*80-bounds.Height));
            hits.Add((bounds,"players-none"));var state=g.Save();g.SetClip(bounds);
            using var nameFont=RichChatBox.GetCoreGuiFont(20);
            for(int i=Math.Max(0,(playerScroll-20)/80);i<members.Length;i++)
            {
                int y=bounds.Y+20+i*80-playerScroll;if(y>=bounds.Bottom)break;
                var member=members[i];using(var row=new SolidBrush(Color.FromArgb(38,255,255,255)))g.FillRectangle(row,bounds.X,y,bounds.Width,60);
                DrawAvatar(g,member,new Rectangle(bounds.X+12,y+12,36,36));
                DrawRosterText(g,member.Name,nameFont,new Rectangle(bounds.X+60,y,Math.Max(1,bounds.Width-290),60));
                if(member.RobloxId!=null){DrawBottomButton(g,new Rectangle(bounds.Right-220,y+5,190,50),busy?"Please wait…":"Add Friend","","friend:"+member.RobloxId);var hit=hits[^1];hits[^1]=(Rectangle.Intersect(bounds,hit.Bounds),hit.Action);}
            }
            g.Restore(state);DrawScrollThumb(g,bounds,20+members.Length*80,playerScroll);
        }
        else if(shownPage is HubPage.Reset or HubPage.Leave)
        {
            string question=shownPage==HubPage.Reset?"Are you sure you want to reset your character?":"Are you sure you want to leave the game?";
            using var confirmationFont=RichChatBox.GetCoreGuiFont(36,true);
            DrawLabel(g,question,confirmationFont,new Rectangle(bounds.X,bounds.Y,bounds.Width,170),Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak);
            DrawBottomButton(g,new Rectangle(bounds.X+bounds.Width/2-220,bounds.Y+170,200,50),shownPage==HubPage.Reset?"Reset":"Leave","",shownPage==HubPage.Reset?"reset-confirm":"leave-confirm");
            DrawBottomButton(g,new Rectangle(bounds.X+bounds.Width/2+20,bounds.Y+170,200,50),shownPage==HubPage.Reset?"Don't Reset":"Don't Leave","","back");
        }
        else if(shownPage==HubPage.Settings)DrawSettingsPage(g,bounds);
        else
        {
            string description=shownPage switch { HubPage.Settings=>"Adjust camera, movement, volume and graphics in Roblox Settings.",HubPage.Report=>"Open Roblox's Report menu to select a player and submit a report.",HubPage.Help=>"Character Movement                     Camera Movement\nW A S D / Arrow Keys — Move          Right Mouse Button — Rotate\nSpace — Jump                              Mouse Wheel / I / O — Zoom\n\nMenu Items                                Misc\nESC — Roblox Menu                       Print Screen — Screenshot\nTAB — Player List                         F12 — Record Video\n` — Backpack                               F11 — Fullscreen",_=>"Save a screenshot or start and stop recording gameplay." };
            DrawLabel(g,description,text,new Rectangle(bounds.X+90,bounds.Y+85,bounds.Width-180,bounds.Height-110),Color.White,TextFormatFlags.Left|TextFormatFlags.Top|TextFormatFlags.WordBreak);
            if(shownPage==HubPage.Settings)DrawActionButton(g,new Rectangle(bounds.X+(bounds.Width-280)/2,bounds.Bottom-85,280,60),"Open Roblox Settings","GameSettingsTab","settings");
            if(shownPage==HubPage.Report)DrawActionButton(g,new Rectangle(bounds.X+(bounds.Width-280)/2,bounds.Bottom-85,280,60),"Open Roblox Report","ReportAbuseTab","report");
            if(shownPage==HubPage.Record){DrawActionButton(g,new Rectangle(bounds.X+70,bounds.Y+170,280,60),"Take Screenshot","","screenshot");DrawActionButton(g,new Rectangle(bounds.Right-350,bounds.Y+170,280,60),recording?"Stop Recording":"Record Video","RecordTab","record");}
        }
    }
    private void DrawSettingsPage(Graphics g,Rectangle bounds)
    {
        const int rowHeight=50,controlWidth=482;
        settingsViewportHeight=Math.Max(1,bounds.Height);
        bool scrollable=SettingsItems.Length*rowHeight>settingsViewportHeight;
        settingsScroll=Math.Clamp(settingsScroll,0,Math.Max(0,SettingsItems.Length*rowHeight-settingsViewportHeight));
        settingsViewport=new Rectangle(bounds.X,bounds.Y,bounds.Width-(scrollable?16:0),settingsViewportHeight);
        settingTracks.Clear();
        using var labelFont=RichChatBox.GetCoreGuiFont(20,true);
        using var valueFont=RichChatBox.GetCoreGuiFont(20);
        var clip=g.Save();g.SetClip(settingsViewport,CombineMode.Intersect);
        for(int i=0;i<SettingsItems.Length;i++)
        {
            var setting=SettingsItems[i];
            var row=new Rectangle(settingsViewport.X,settingsViewport.Y+i*rowHeight-settingsScroll,settingsViewport.Width,rowHeight);
            if(row.Bottom<=settingsViewport.Top||row.Top>=settingsViewport.Bottom)continue;
            bool selected=i==selectedSetting;
            if(selected)using(var hover=new SolidBrush(Color.FromArgb(51,255,255,255)))g.FillRectangle(hover,row);
            int controlLeft=row.Right-10-controlWidth;
            DrawLabel(g,setting.Name,labelFont,new Rectangle(row.X+10,row.Y,controlLeft-row.X-20,row.Height),Color.White,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
            hits.Add((Rectangle.Intersect(row,settingsViewport),"setting-row:"+setting.Name));
            var leftButton=new Rectangle(controlLeft,row.Y,50,50);
            var rightButton=new Rectangle(row.Right-60,row.Y,50,50);
            string? value=settingValues.GetValueOrDefault(setting.Name);
            if(setting.Name=="Developer Console"||value=="Unavailable")
            {
                var button=new Rectangle(controlLeft+10,row.Y+5,controlWidth-20,40);
                DrawNineSlice(g,"MenuButton",button,8,6,8,10);
                DrawLabel(g,setting.Name=="Developer Console"?"Open":"Unavailable",valueFont,button,
                    value=="Unavailable"?Color.Gray:Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
                if(setting.Name=="Developer Console")hits.Add((Rectangle.Intersect(button,settingsViewport),"setting-step:Developer Console:right"));
                continue;
            }
            int filled=int.TryParse(value,out int stored)?Math.Clamp(stored,0,setting.Maximum):-1;
            if(pendingSliders.TryGetValue(setting.Name,out int pending))filled=pending;
            if(draggingSetting==setting.Name)filled=draggingSettingValue;
            if(!setting.Slider||filled<0||filled>setting.Minimum)
            {
                ImageCentered(g,"SettingsLeft",leftButton);
                hits.Add((Rectangle.Intersect(leftButton,settingsViewport),"setting-step:"+setting.Name+":left"));
            }
            if(!setting.Slider||filled<setting.Maximum)
            {
                ImageCentered(g,"SettingsRight",rightButton);
                hits.Add((Rectangle.Intersect(rightButton,settingsViewport),"setting-step:"+setting.Name+":right"));
            }
            var track=new Rectangle(controlLeft+50,row.Y+13,controlWidth-100,24);
            if(setting.Slider)
            {
                settingTracks[setting.Name]=track;
                float segmentWidth=(track.Width-4*(setting.Maximum-1))/(float)setting.Maximum;
                for(int segment=0;segment<setting.Maximum;segment++)
                {
                    var bar=new RectangleF(track.X+segment*(segmentWidth+4),track.Y,segmentWidth,24);
                    using var brush=new SolidBrush(segment<filled?Color.FromArgb(0,162,255):Color.FromArgb(163,78,84,96));
                    if(segment==0||segment==setting.Maximum-1)
                    {
                        using var path=new GraphicsPath();
                        float radius=3;
                        if(segment==0)
                        {
                            path.AddArc(bar.X,bar.Y,radius*2,radius*2,180,90);
                            path.AddLine(bar.X+radius,bar.Y,bar.Right,bar.Y);
                            path.AddLine(bar.Right,bar.Y,bar.Right,bar.Bottom);
                            path.AddArc(bar.X,bar.Bottom-radius*2,radius*2,radius*2,90,90);
                        }
                        else
                        {
                            path.AddLine(bar.X,bar.Y,bar.Right-radius,bar.Y);
                            path.AddArc(bar.Right-radius*2,bar.Y,radius*2,radius*2,270,90);
                            path.AddArc(bar.Right-radius*2,bar.Bottom-radius*2,radius*2,radius*2,0,90);
                            path.AddLine(bar.Right-radius,bar.Bottom,bar.X,bar.Bottom);
                        }
                        path.CloseFigure();g.FillPath(brush,path);
                    }
                    else g.FillRectangle(brush,bar);
                    if(segment==0||segment==setting.Maximum-1)
                    {
                        string texture=segment==0
                            ?(segment<filled?"SliderSelectedBarLeft":"SliderBarLeft")
                            :(segment<filled?"SliderSelectedBarRight":"SliderBarRight");
                        if(CoreGui2016Textures.Get(texture) is Image edge)
                            g.DrawImage(edge,Rectangle.Ceiling(bar),0,0,edge.Width,edge.Height,GraphicsUnit.Pixel);
                    }
                    hits.Add((Rectangle.Intersect(Rectangle.Ceiling(bar),settingsViewport),"setting-set:"+setting.Name+":"+(segment+1)));
                }
            }
            else
            {
                DrawLabel(g,value??"—",valueFont,new Rectangle(controlLeft+50,row.Y,controlWidth-100,50),
                    selected?Color.White:Color.FromArgb(179,200,200,200),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
                hits.Add((Rectangle.Intersect(new Rectangle(controlLeft+50,row.Y,controlWidth-100,50),settingsViewport),"setting-step:"+setting.Name+":right"));
            }
        }
        g.Restore(clip);
        if(scrollable)DrawScrollThumb(g,settingsViewport,SettingsItems.Length*rowHeight,settingsScroll);

    }

    internal void ApplySettingsResult(RobloxSettingsResult result)
    {
        foreach(var pair in result.Values)
        {
            settingValues[pair.Key]=pair.Value;

        }
        settingsRevision++;
        Render();
    }

    internal void SuspendForNativeSettings()
    {
        menuAnimating=false;animationClock.Stop();menuOffset=0;
        pageAnimating=false;pageClock.Stop();
        Hide();
    }

    private void SelectSetting(int index)
    {
        selectedSetting=Math.Clamp(index,0,SettingsItems.Length-1);
        int top=selectedSetting*50,visible=Math.Max(50,settingsViewportHeight);
        settingsScroll=Math.Clamp(settingsScroll,Math.Max(0,top-visible+50),top);
        Render();
    }


    internal void ReopenSettings(string? label=null){SetMenuOpen(true);page=HubPage.Settings;selectedSetting=label==null?-1:Array.FindIndex(SettingsItems,item=>item.Name==label);pageAnimating=false;pageClock.Stop();Render();}
    private void DrawBottomButton(Graphics g,Rectangle bounds,string label,string icon,string action)
    {
        bool selected=page is HubPage.Reset or HubPage.Leave && (confirmPositive?action is "reset-confirm" or "leave-confirm":action=="back");
        DrawNineSlice(g,hoveredAction==action||selected?"MenuButtonSelected":"MenuButton",bounds,8,6,8,10);if(icon.Length>0)ImageAt(g,icon,new Rectangle(bounds.X+10,bounds.Y+5,60,60));using var font=RichChatBox.GetCoreGuiFont(bounds.Height<40?14:20,bounds.Height>=40);
        Rectangle labelBounds=icon.Length>0?new Rectangle(bounds.X+10,bounds.Y-4,bounds.Width,bounds.Height):new Rectangle(bounds.X+8,bounds.Y,bounds.Width-16,bounds.Height);
        DrawLabel(g,label,font,labelBounds,Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.PreserveGraphicsClipping);hits.Add((bounds,action));
    }
    // ROHTML's page buttons are not BottomBarButton controls: their icons retain their
    // source dimensions and sit beside the label instead of being stretched to 48px.
    private void DrawActionButton(Graphics g,Rectangle bounds,string label,string icon,string action)
    {
        DrawNineSlice(g,hoveredAction==action?"MenuButtonSelected":"MenuButton",bounds,8,6,8,10);
        int labelLeft=bounds.X;
        if(icon.Length>0&&CoreGui2016Textures.Get(icon) is Image image)
        {
            int x=bounds.X+8,y=bounds.Y+(bounds.Height-image.Height)/2;
            g.DrawImage(image,x,y,image.Width,image.Height);
            labelLeft=x+image.Width+8;
        }
        using var font=RichChatBox.GetCoreGuiFont(bounds.Height<40?14:20,bounds.Height>=40);
        DrawLabel(g,label,font,new Rectangle(labelLeft,bounds.Y,bounds.Right-labelLeft-8,bounds.Height),Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.PreserveGraphicsClipping);
        hits.Add((bounds,action));
    }
    private static void DrawLabel(Graphics g,string text,Font font,Rectangle bounds,Color color,TextFormatFlags flags)
    {
        if(bounds.Width<=0||bounds.Height<=0)return;
        using var brush=new SolidBrush(color);using var format=new StringFormat(StringFormat.GenericTypographic);
        format.Alignment=flags.HasFlag(TextFormatFlags.HorizontalCenter)?StringAlignment.Center:StringAlignment.Near;
        format.LineAlignment=flags.HasFlag(TextFormatFlags.VerticalCenter)?StringAlignment.Center:flags.HasFlag(TextFormatFlags.Bottom)?StringAlignment.Far:StringAlignment.Near;
        if(!flags.HasFlag(TextFormatFlags.WordBreak))format.FormatFlags|=StringFormatFlags.NoWrap;
        format.Trimming=StringTrimming.EllipsisCharacter;
        using var path=new GraphicsPath();
        path.AddString(text,font.FontFamily,(int)font.Style,font.Size,bounds,format);
        g.FillPath(brush,path);
    }
    private void DrawAvatar(Graphics g,ChatMember member,Rectangle bounds)
    {
        var image=avatars.Get(member,Visible);if(image==null)return;
        float scale=Math.Min((float)bounds.Width/image.Width,(float)bounds.Height/image.Height);
        float width=image.Width*scale,height=image.Height*scale;
        g.DrawImage(image,bounds.X+(bounds.Width-width)/2,bounds.Y+(bounds.Height-height)/2,width,height);
    }
    private static void ImageAt(Graphics g,string name,Rectangle bounds){var image=CoreGui2016Textures.Get(name);if(image!=null)g.DrawImage(image,bounds);}
    private static void ImageCentered(Graphics g,string name,Rectangle bounds)
    {
        var image=CoreGui2016Textures.Get(name);if(image!=null)g.DrawImage(image,bounds.X+(bounds.Width-image.Width)/2,bounds.Y+(bounds.Height-image.Height)/2,image.Width,image.Height);
    }
    private void DrawScrollThumb(Graphics g,Rectangle viewport,int content,int offset)
    {
        if(content<=viewport.Height||viewport.Height<=0)return;
        int height=Math.Min(viewport.Height,Math.Max(menuOpen?28:18,viewport.Height*viewport.Height/content));
        int y=viewport.Y+(viewport.Height-height)*offset/(content-viewport.Height);
        scrollThumb=new Rectangle(viewport.Right-(menuOpen?14:6),y,menuOpen?12:6,height);hits.Add((scrollThumb,"scroll"));
        if(menuOpen){ImageAt(g,"scroll-top",new Rectangle(scrollThumb.X,y,12,14));ImageAt(g,"scroll-middle",new Rectangle(scrollThumb.X,y+14,12,Math.Max(0,height-28)));ImageAt(g,"scroll-bottom",new Rectangle(scrollThumb.X,y+height-14,12,14));return;}
        using var brush=new SolidBrush(Color.FromArgb(77,255,255,255));
        g.FillRectangle(brush,viewport.Right-6,y+3,6,Math.Max(0,height-6));
        g.FillEllipse(brush,viewport.Right-6,y,6,6);g.FillEllipse(brush,viewport.Right-6,y+height-6,6,6);
    }
    private static void DrawNineSlice(Graphics g,string name,Rectangle destination,int left,int top,int right,int bottom)
    {
        var image=CoreGui2016Textures.Get(name);if(image==null||destination.Width<=0||destination.Height<=0)return;
        left=Math.Min(left,image.Width/2);right=Math.Min(right,image.Width-left);top=Math.Min(top,image.Height/2);bottom=Math.Min(bottom,image.Height-top);
        int centerSourceWidth=Math.Max(1,image.Width-left-right),centerSourceHeight=Math.Max(1,image.Height-top-bottom);
        int destinationLeft=Math.Min(left,destination.Width/2),destinationRight=Math.Min(right,destination.Width-destinationLeft);
        int destinationTop=Math.Min(top,destination.Height/2),destinationBottom=Math.Min(bottom,destination.Height-destinationTop);
        int centerDestinationWidth=Math.Max(0,destination.Width-destinationLeft-destinationRight),centerDestinationHeight=Math.Max(0,destination.Height-destinationTop-destinationBottom);
        int[] sx={0,left,image.Width-right},sw={left,centerSourceWidth,right};int[] dx={destination.X,destination.X+destinationLeft,destination.Right-destinationRight},dw={destinationLeft,centerDestinationWidth,destinationRight};
        int[] sy={0,top,image.Height-bottom},sh={top,centerSourceHeight,bottom};int[] dy={destination.Y,destination.Y+destinationTop,destination.Bottom-destinationBottom},dh={destinationTop,centerDestinationHeight,destinationBottom};
        for(int row=0;row<3;row++)for(int column=0;column<3;column++)if(dw[column]>0&&dh[row]>0&&sw[column]>0&&sh[row]>0)
            g.DrawImage(image,new Rectangle(dx[column],dy[row],dw[column],dh[row]),sx[column],sy[row],sw[column],sh[row],GraphicsUnit.Pixel);
    }
    internal void Render()
    {
        string state=$"{Bounds}|{menuOpen}|{menuAnimating}|{menuOffset:F1}|{page}|{pageAnimating}|{pageProgress:F2}|{rosterVisible}|{chatVisible}|{recording}|{busy}|{backpackHighlighted}|{emotesHighlighted}|{localMemberId}|{confirmPositive}|{selectedMember}|{avatarRevision}|{playerScroll}|{rosterScroll}|{hoveredAction}|{selectedSetting}|{settingsScroll}|{settingsRevision}|{draggingSetting}|{draggingSettingValue}|{(menuOpen||!rosterVisible?0:Environment.TickCount64/1000)}|{membersRevision}";
        if(state==lastRenderState)return;lastRenderState=state;
        using var bitmap=CreateBitmap();IntPtr screen=GetDC(IntPtr.Zero),memory=CreateCompatibleDC(screen),image=bitmap.GetHbitmap(Color.FromArgb(0)),previous=SelectObject(memory,image);
        try{var destination=Location;var source=Point.Empty;var size=bitmap.Size;var blend=new Blend{Alpha=255,Format=1};if(!UpdateLayeredWindow(Handle,screen,ref destination,ref size,memory,ref source,0,ref blend,2))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());}
        finally{SelectObject(memory,previous);DeleteObject(image);DeleteDC(memory);ReleaseDC(IntPtr.Zero,screen);}
    }
    protected override void Dispose(bool disposing){if(disposing){sliderDelay.Dispose();clock.Dispose();animationClock.Dispose();pageClock.Dispose();avatars.Dispose();}base.Dispose(disposing);}
    [StructLayout(LayoutKind.Sequential,Pack=1)]private struct Blend{public byte Operation,Flags,Alpha,Format;}
    [DllImport("user32.dll",SetLastError=true)]private static extern bool UpdateLayeredWindow(IntPtr window,IntPtr dc,ref Point destination,ref Size size,IntPtr sourceDc,ref Point source,int key,ref Blend blend,int flags);
    [DllImport("user32.dll")]private static extern IntPtr GetDC(IntPtr window);[DllImport("user32.dll")]private static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("gdi32.dll")]private static extern IntPtr CreateCompatibleDC(IntPtr dc);[DllImport("gdi32.dll")]private static extern IntPtr SelectObject(IntPtr dc,IntPtr value);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(IntPtr value);[DllImport("gdi32.dll")]private static extern bool DeleteDC(IntPtr dc);
}
