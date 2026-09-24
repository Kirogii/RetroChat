using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RobloxChatLauncher.UI;

internal sealed record ChatMember(string Id,string Name,string? RobloxId,long SessionSeconds=0,long TotalSeconds=0,int? TimezoneOffsetMinutes=null,string? TimezoneLabel=null);

// Standalone overlay: never injects Lua or modifies Roblox's process.
internal sealed class CoreGui2013 : Form
{
    private readonly ListBox players = new() { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed,
        ItemHeight = 12, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(6,16,34), ForeColor = Color.White };
    private readonly Panel roster = new() { Width = 248, BackColor = Color.Transparent };
    private readonly Panel menu = new() { Size = new Size(394,367), BackColor = Color.FromArgb(17,18,19) };
    private readonly Panel resetMenu = new() { Size = new Size(394,367), BackColor = Color.FromArgb(17,18,19), Visible=false };
    private readonly Panel leaveMenu = new() { Size = new Size(394,367), BackColor = Color.FromArgb(17,18,19), Visible=false };
    private Panel? helpPanel;
    private Panel? playerPopup;
    private readonly Font playerFont = new("Arial",9,FontStyle.Bold,GraphicsUnit.Pixel);
    private readonly System.Windows.Forms.Timer busyTimer = new() { Interval=200 };
    private readonly PictureBox[] spinnerIcons = new PictureBox[8];
    private readonly Panel busyPanel = new() { Size=new Size(360,100),BackColor=Color.FromArgb(45,45,45),Visible=false };
    private readonly EscMenu2013Window escMenu = new();
    private readonly TextureRoster textureRoster = new();
    private readonly PictureBox recordStop = new() { Bounds=new Rectangle(0,0,59,27),Visible=false,
        BackColor=Color.Transparent,SizeMode=PictureBoxSizeMode.StretchImage };
    private int spinPosition;
    internal event Action<string>? ActionRequested;
    private bool menuOpen;
    internal bool MenuOpen => menuOpen;
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x08000000; return cp; } }
    internal bool ContainsInteractiveScreenPoint(Point screen)
    {
        Point local=PointToClient(screen);
        return menuOpen||GetChildAtPoint(local,GetChildAtPointSkip.Invisible)!=null;
    }
    internal CoreGui2013()
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None; BackColor = Color.Magenta; TransparencyKey = Color.Magenta;
        // Keep the list as the data model; render its textures in a per-pixel-alpha window.
        roster.Controls.Add(players);
        textureRoster.MemberClicked += index => ShowPlayerPopup((ChatMember)players.Items[index],index);
        textureRoster.LayoutChanged += RefreshTextureRoster;
        escMenu.ActionRequested+=action=>ActionRequested?.Invoke(action);
        escMenu.Dismissed+=()=>{menuOpen=false;RefreshTextureRoster();};
        var title = new Panel { Dock = DockStyle.Top, Height = 57,
            BackgroundImage=CoreGuiTextures.Get("94692054"), BackgroundImageLayout=ImageLayout.Stretch };
        title.Controls.Add(new Label { Text="Chatroom",Bounds=new Rectangle(4,2,151,16),BackColor=Color.Transparent,
            ForeColor=Color.White,TextAlign=ContentAlignment.MiddleRight,Font=new Font("Arial",12,FontStyle.Bold,GraphicsUnit.Pixel) });
        var count = new Label { Name="MemberCount",Text="0",Bounds=new Rectangle(4,18,151,15),BackColor=Color.Transparent,
            ForeColor=Color.White,TextAlign=ContentAlignment.MiddleRight,Font=new Font("Arial",12,FontStyle.Bold,GraphicsUnit.Pixel) };
        title.Controls.Add(count);
        title.Controls.Add(new Label { Text="Session   Total Time",Bounds=new Rectangle(77,33,80,11),BackColor=Color.Transparent,
            ForeColor=Color.White,TextAlign=ContentAlignment.MiddleRight,Font=new Font("Arial",7,FontStyle.Bold,GraphicsUnit.Pixel) });
        title.Controls.Add(new Label { Text="Chatroom Members",Bounds=new Rectangle(2,45,155,12),BackColor=Color.FromArgb(29,78,38),
            ForeColor=Color.White,Font=new Font("Arial",10,FontStyle.Bold,GraphicsUnit.Pixel) });
        players.DataSourceChanged+=(_,_)=>count.Text=players.Items.Count.ToString();
        memberCount=count;
        roster.Controls.Add(title);
        var bottom = new PictureBox { Dock=DockStyle.Bottom, Height=12, Image=CoreGuiTextures.Get("94754966"),SizeMode=PictureBoxSizeMode.StretchImage };
        bottom.Controls.Add(new PictureBox { Bounds=new Rectangle(63,0,32,10),BackColor=Color.Transparent,
            Image=CoreGuiTextures.Get("94692731"),SizeMode=PictureBoxSizeMode.StretchImage });
        roster.Controls.Add(bottom);
        roster.Padding=new Padding(1,0,1,0);
        roster.SizeChanged+=(_,_)=>SetRoundedRegion(roster,5);
        players.DrawItem += (_, e) => {
            if (e.Index < 0) return;
            e.DrawBackground();
            var texture = CoreGuiTextures.Get(e.Index % 2 == 0 ? "94691980" : "94692025");
            if (texture != null) e.Graphics.DrawImage(texture,e.Bounds);
            // Custom chatroom badge, not an official Roblox membership badge.
            using var badge = new SolidBrush(Color.FromArgb(70,180,245));
            e.Graphics.FillRectangle(badge, 3, e.Bounds.Y + 3, 7, 5);
            e.Graphics.FillPolygon(badge, new[] { new Point(4,e.Bounds.Y+7),new Point(4,e.Bounds.Y+10),new Point(7,e.Bounds.Y+7) });
            var member=(ChatMember)players.Items[e.Index];
            TextRenderer.DrawText(e.Graphics, member.Name, playerFont,
                new Rectangle(12,e.Bounds.Y,72,12), Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(e.Graphics, FormatTime(member.SessionSeconds), playerFont,
                new Rectangle(84,e.Bounds.Y,32,12), Color.White, TextFormatFlags.VerticalCenter|TextFormatFlags.Right|TextFormatFlags.NoPadding);
            TextRenderer.DrawText(e.Graphics, FormatTime(member.TotalSeconds), playerFont,
                new Rectangle(118,e.Bounds.Y,36,12), Color.White, TextFormatFlags.VerticalCenter|TextFormatFlags.Right|TextFormatFlags.NoPadding);
        };
        players.MouseUp += (_, e) => {
            int index = players.IndexFromPoint(e.Location);
            if (index < 0 || e.Button != MouseButtons.Left) return;
            var member = (ChatMember)players.Items[index];
            ShowPlayerPopup(member,index);
        };
        var open = new Button { Text = "Menu", Bounds = new Rectangle(5,5,70,26), BackColor = Color.FromArgb(55,55,55), ForeColor = Color.White };
        open.Click += (_, _) => ToggleMenu(); Controls.Add(open);
        recordStop.Image=CoreGuiTextures.Get("RecordStop");recordStop.Click+=(_,_)=>ActionRequested?.Invoke("record");Controls.Add(recordStop);
        Controls.Add(menu); menu.Visible = false;
        SetRoundedRegion(menu,8);
        SetRoundedRegion(resetMenu,8);
        SetRoundedRegion(leaveMenu,8);
        var spinner = new Panel { Bounds=new Rectangle(10,10,80,80) };
        busyPanel.Controls.Add(spinner);
        for (int i=0;i<8;i++) {
            double angle=(i+1)*Math.PI/4;
            spinnerIcons[i]=new PictureBox { Bounds=new Rectangle((int)(32+24*Math.Cos(angle)),(int)(32+24*Math.Sin(angle)),16,16),
                SizeMode=PictureBoxSizeMode.StretchImage,Image=CoreGuiTextures.Get("45880710") };
            spinner.Controls.Add(spinnerIcons[i]);
        }
        busyPanel.Controls.Add(new Label { Text="Sending friend request...",Bounds=new Rectangle(105,35,250,30),ForeColor=Color.White,
            Font=new Font("Arial",18,FontStyle.Bold,GraphicsUnit.Pixel) });
        Controls.Add(busyPanel);
        busyTimer.Tick+=(_,_)=>{ for(int i=0;i<8;i++) spinnerIcons[i].Image=CoreGuiTextures.Get(i==spinPosition||i==(spinPosition+1)%8?"45880668":"45880710"); spinPosition=(spinPosition+1)%8; };
        AddButton("Resume Game",64,49,267,34,"resume",true);
        AddButton("Reset Character",64,94,267,34,"reset");
        AddButton("Game Setting",64,139,267,34,"settings");
        AddButton("Help",64,206,129,34,"help");
        AddButton("Screenshot",201,206,130,34,"screenshot");
        AddButton("Record Video",201,251,130,33,"record");
        AddButton("Leave Game",64,303,267,34,"leave");
        menu.Controls.Add(new Label { Text = "Game Menu", Bounds = new Rectangle(0,7,394,28), ForeColor = Color.White,
            BackColor=Color.Transparent,TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Arial",21,FontStyle.Bold,GraphicsUnit.Pixel) });
        Controls.Add(resetMenu); BuildConfirmation(resetMenu,"Are you sure to reset your charater?","You will be put back on a spawn point","Reset","reset-confirm");
        Controls.Add(leaveMenu); BuildConfirmation(leaveMenu,"Are you sure you want to leave this game?","You will leave the current Roblox game","Yes","leave-confirm","No");
        ScaleMenuPanel(menu,1.25f); ScaleMenuPanel(resetMenu,1.25f); ScaleMenuPanel(leaveMenu,1.25f);
        SetRoundedRegion(menu,12); SetRoundedRegion(resetMenu,12); SetRoundedRegion(leaveMenu,12);
        LocationChanged += (_,_) => { if(escMenu.Visible){escMenu.Bounds=Bounds;escMenu.Render();} RefreshTextureRoster(); };
        SizeChanged += (_,_) => { if(escMenu.Visible){escMenu.Bounds=Bounds;escMenu.Render();} };
        Resize += (_, _) => { roster.Location = new Point(Math.Max(0,ClientSize.Width-roster.Width),(int)(ClientSize.Height*.005));
            roster.Height = Math.Min(114+players.Items.Count*20, Math.Max(114,ClientSize.Height-10));
            menu.Location = resetMenu.Location = leaveMenu.Location = new Point((ClientSize.Width-menu.Width)/2,(ClientSize.Height-menu.Height)/2);
            busyPanel.Location = new Point((ClientSize.Width-360)/2,(ClientSize.Height-100)/2);
            if(helpPanel!=null) LayoutHelp(); RefreshTextureRoster(); };
    }
    private static string FormatTime(long seconds) => seconds < 3600 ? $"{seconds/60}:{seconds%60:00}" : $"{seconds/3600}:{seconds/60%60:00}";
    private void ShowPlayerPopup(ChatMember member,int index)
    {
        playerPopup?.Dispose();
        // Lua PopUpPanelTemplate: full leaderboard width, .032 of 800px per row.
        var popup = new Panel { Size=new Size(150,78),BackColor=Color.Magenta };
        playerPopup=popup;
        int rosterX=textureRoster.Left-Left, rosterY=textureRoster.Top-Top;
        int header=textureRoster.Maximized?86:94,rowHeight=textureRoster.Maximized?18:20;
        popup.Location=new Point(Math.Max(0,rosterX-150),Math.Clamp(rosterY+header+(index-textureRoster.FirstRow)*rowHeight,0,Math.Max(0,Height-78)));
        string[] labels={member.Name,member.RobloxId==null?"Unverified guest":"Send Friend Request","Close"};
        string[] textures={"97108784","97112126","100869219"};
        for(int i=0;i<3;i++) {
            int row=i;
            var item=new Button { Text=labels[i],Bounds=new Rectangle(0,i*26,150,26),FlatStyle=FlatStyle.Flat,
                BackgroundImage=CoreGuiTextures.Get(textures[i]),BackgroundImageLayout=ImageLayout.Stretch,
                ForeColor=Color.White,Font=new Font("Arial",14,FontStyle.Bold,GraphicsUnit.Pixel),UseVisualStyleBackColor=false };
            item.FlatAppearance.BorderSize=0;
            item.Enabled=i!=1||member.RobloxId!=null;
            item.Click+=(_,_)=>{ popup.Dispose();playerPopup=null;if(row==1)ActionRequested?.Invoke("friend:"+member.RobloxId); };
            popup.Controls.Add(item);
        }
        Controls.Add(popup);popup.BringToFront();
    }
    private void AddButton(string label,int x,int y,int width,int height,string action,bool primary=false)
    {
        var button = MakeMenuButton(label,new Rectangle(x,y,width,height),primary);
        if(action=="screenshot"||action=="record") { button.Shortcut=action=="screenshot"?"PrintSc":"F12";button.Font=new Font("Arial",10,FontStyle.Regular,GraphicsUnit.Pixel); }
        button.Click += (_,_) => {
            if(action=="reset") { menu.Hide();resetMenu.Show();resetMenu.BringToFront();return; }
            if(action=="leave") { menu.Hide();leaveMenu.Show();leaveMenu.BringToFront();return; }
            if (action=="help") { ShowLegacyHelp(); return; }
            CloseMenus(); ActionRequested?.Invoke(action);
        }; menu.Controls.Add(button);
    }
    private ReferenceButton MakeMenuButton(string text,Rectangle bounds,bool primary=false)
    {
        return new ReferenceButton { Text=text,Bounds=bounds,Primary=primary,
            Font=new Font("Arial",16,FontStyle.Regular,GraphicsUnit.Pixel) };
    }
    private static void SetRoundedRegion(Control control,int radius)
    {
        using var path=new GraphicsPath();int diameter=radius*2;
        path.AddArc(0,0,diameter,diameter,180,90);path.AddArc(control.Width-diameter,0,diameter,diameter,270,90);
        path.AddArc(control.Width-diameter,control.Height-diameter,diameter,diameter,0,90);path.AddArc(0,control.Height-diameter,diameter,diameter,90,90);
        path.CloseFigure();var old=control.Region;control.Region=new Region(path);old?.Dispose();
    }
    private void BuildConfirmation(Panel panel,string title,string subtitle,string confirm,string action,string cancelText="cancel")
    {
        panel.Controls.Add(new Label { Text=title,Bounds=new Rectangle(12,79,370,32),TextAlign=ContentAlignment.MiddleCenter,
            BackColor=Color.Transparent,ForeColor=Color.White,Font=new Font("Arial",18,FontStyle.Bold,GraphicsUnit.Pixel) });
        panel.Controls.Add(new Label { Text=subtitle,Bounds=new Rectangle(15,153,364,24),TextAlign=ContentAlignment.MiddleCenter,
            BackColor=Color.Transparent,ForeColor=Color.White,Font=new Font("Arial",11,FontStyle.Bold,GraphicsUnit.Pixel) });
        var cancel=MakeMenuButton(cancelText,new Rectangle(38,278,146,34));
        var accept=MakeMenuButton(confirm,new Rectangle(210,278,146,34),true);
        cancel.Click+=(_,_)=>{panel.Hide();menu.Show();menu.BringToFront();};
        accept.Click+=(_,_)=>{CloseMenus();ActionRequested?.Invoke(action);};
        panel.Controls.Add(cancel);panel.Controls.Add(accept);
    }
    private static void ScaleMenuPanel(Panel panel,float factor)
    {
        panel.SuspendLayout();
        foreach(Control control in panel.Controls) {
            control.Bounds=new Rectangle((int)Math.Round(control.Left*factor),(int)Math.Round(control.Top*factor),
                (int)Math.Round(control.Width*factor),(int)Math.Round(control.Height*factor));
            control.Font=new Font(control.Font.FontFamily,control.Font.Size*factor,control.Font.Style,GraphicsUnit.Pixel);
        }
        panel.Size=new Size((int)Math.Round(394*factor),(int)Math.Round(367*factor));panel.ResumeLayout();
    }
    private void ShowLegacyHelp()
    {
        helpPanel?.Dispose();
        var help = new Panel { Bounds=menu.Bounds, BackColor=Color.FromArgb(45,45,45) };
        helpPanel=help;
        help.Controls.Add(new Label { Name="Title",Text="Keyboard & Mouse Controls",Bounds=new Rectangle(0,8,524,40),ForeColor=Color.White,
            Font=new Font("Arial",36,FontStyle.Bold,GraphicsUnit.Pixel),TextAlign=ContentAlignment.MiddleCenter });
        var picture = new PictureBox { Bounds=new Rectangle(15,100,494,260),SizeMode=PictureBoxSizeMode.Zoom,Image=CoreGuiTextures.Get("45915798") };
        help.Controls.Add(picture);
        picture.Name="Illustration";
        string[] names={"Look","Move","Gear","Zoom"}; string[] ids={"45915798","45915811","45917596","45915825"};
        for(int i=0;i<4;i++) { string id=ids[i]; var tab=new Button { Name="Tab"+i,Text=names[i],Bounds=new Rectangle(15+i*124,55,120,36) };
            tab.Click+=(_,_)=>picture.Image=CoreGuiTextures.Get(id); help.Controls.Add(tab); }
        var mouseLock=new CheckBox { Name="MouseLock",Text="Mouse Lock controls",ForeColor=Color.White,AutoSize=true };
        mouseLock.CheckedChanged+=(_,_)=>picture.Image=CoreGuiTextures.Get(mouseLock.Checked?"54071825":"45915798"); help.Controls.Add(mouseLock);
        var close=new Button { Name="OK",Text="OK",Bounds=new Rectangle(182,375,160,40) }; close.Click+=(_,_)=>{ help.Dispose(); helpPanel=null; menu.Show(); };
        help.Controls.Add(close); menu.Hide(); Controls.Add(help); LayoutHelp(); help.BringToFront();
    }
    private void LayoutHelp()
    {
        if(helpPanel==null) return;
        helpPanel.Bounds=new Rectangle((int)(Width*.2),(int)(Height*.2),(int)(Width*.6),(int)(Height*.6));
        int w=helpPanel.Width,h=helpPanel.Height;
        helpPanel.Controls["Title"]!.Bounds=new Rectangle(0,(int)(h*.025),w,40);
        for(int i=0;i<4;i++) helpPanel.Controls["Tab"+i]!.Bounds=new Rectangle((int)(w*.1+i*w*.2),(int)(h*.07)+40,(int)(w*.2),45);
        helpPanel.Controls["Illustration"]!.Bounds=new Rectangle((int)(w*.05),(int)(h*.075)+80,(int)(w*.9),Math.Max(1,(int)(h*.9)-150));
        helpPanel.Controls["MouseLock"]!.Location=new Point((int)(w*.05),Math.Max(110,h-90));
        helpPanel.Controls["OK"]!.Bounds=new Rectangle((int)(w*.35),(int)(h*.975)-50,(int)(w*.3),45);
    }
    internal void SetBusy(bool busy) { busyPanel.Visible=busy; if(busy) { busyPanel.BringToFront(); busyTimer.Start(); } else busyTimer.Stop(); }
    internal void ShowResetPreview() { menuOpen=true;menu.Hide();leaveMenu.Hide();resetMenu.Hide();textureRoster.Hide();escMenu.Open(Bounds);escMenu.ShowReset(); }
    private void CloseMenus() { menuOpen=false;menu.Hide();resetMenu.Hide();leaveMenu.Hide();escMenu.Hide();RefreshTextureRoster(); }
    internal void ToggleMenu()
    {
        playerPopup?.Dispose();playerPopup=null;helpPanel?.Dispose();helpPanel=null;resetMenu.Hide();leaveMenu.Hide();
        if(menuOpen) { escMenu.CloseMenu(); return; }
        menuOpen=true;textureRoster.Hide();escMenu.Open(Bounds);
    }
    internal void SetRecording(bool recording) { recordStop.Visible=recording;if(recording)recordStop.BringToFront(); }
    internal void TogglePlayerListMaximized() { textureRoster.ToggleMaximized();RefreshTextureRoster(); }
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if(!Disposing&&!IsDisposed) { if(!Visible) { CloseMenus();textureRoster.Hide(); } else RefreshTextureRoster(); }
    }
    protected override void Dispose(bool disposing) { if(disposing) { busyTimer.Dispose();playerFont.Dispose();escMenu.Dispose();textureRoster.Dispose();roster.Dispose(); } base.Dispose(disposing); }
    internal void SetMembers(IEnumerable<ChatMember> members)
    {
        playerPopup?.Dispose();playerPopup=null;
        players.BeginUpdate(); players.Items.Clear();
        foreach (var member in members.OrderBy(m=>m.Name,StringComparer.OrdinalIgnoreCase)) players.Items.Add(member);
        players.EndUpdate();
        memberCount.Text=players.Items.Count.ToString();
        roster.Height = Math.Min(114+players.Items.Count*20,Math.Max(114,ClientSize.Height-10));
        textureRoster.UpdateMembers(players.Items.Cast<ChatMember>().ToArray());
        RefreshTextureRoster();
    }

    private void RefreshTextureRoster()
    {
        if(!Visible||menuOpen||Disposing||textureRoster.IsDisposed) { if(textureRoster.Visible)textureRoster.Hide();return; }
        Size desired=textureRoster.Maximized
            ? new Size(Math.Max(225,ClientSize.Width/2),Math.Max(104,(int)(ClientSize.Height*.9)))
            : textureRoster.Minimized ? new Size(248,114) : roster.Size;
        Point local=textureRoster.Maximized ? new Point((ClientSize.Width-desired.Width)/2,(int)(ClientSize.Height*.1))
            : new Point(Math.Max(0,ClientSize.Width-desired.Width),(int)(ClientSize.Height*.005));
        textureRoster.Bounds=new Rectangle(PointToScreen(local),desired);
        if(!textureRoster.Visible) textureRoster.Show(this);
        textureRoster.Render();
    }
    internal void DrawRosterPreview(Graphics graphics) { if(menuOpen)return;using var bitmap=textureRoster.CreateBitmap();graphics.DrawImageUnscaled(bitmap,new Point(textureRoster.Left-Left,textureRoster.Top-Top)); }

    private readonly Label memberCount;

    // Paint the thin rounded outlines ourselves; native Button borders clip at corners.
    private sealed class ReferenceButton : Button
    {
        internal bool Primary;
        internal string? Shortcut;
        private bool hovered;
        internal ReferenceButton() { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);BackColor=Color.Transparent; }
        protected override void OnMouseEnter(EventArgs e) { hovered=true;Invalidate();base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered=false;Invalidate();base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            using var shape=new GraphicsPath();
            const float d=4;float right=Width-1.5f,bottom=Height-1.5f;
            shape.AddArc(.5f,.5f,d,d,180,90);shape.AddArc(right-d,.5f,d,d,270,90);
            shape.AddArc(right-d,bottom-d,d,d,0,90);shape.AddArc(.5f,bottom-d,d,d,90,90);shape.CloseFigure();
            using var fill=new SolidBrush(Color.FromArgb(1,1,1));g.FillPath(fill,shape);
            using var border=new Pen(hovered?Color.FromArgb(235,235,235):Primary?Color.FromArgb(255,0,0):Color.FromArgb(60,62,63),1);
            g.DrawPath(border,shape);
            var textBounds=new Rectangle(0,0,Width-(Shortcut==null?0:20),Height);
            TextRenderer.DrawText(g,Text,Font,textBounds,Color.FromArgb(245,245,245),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
            if(Shortcut!=null) { using var small=new Font("Arial",5,FontStyle.Regular,GraphicsUnit.Pixel);
                TextRenderer.DrawText(g,Shortcut,small,new Rectangle(Width-30,0,27,Height),Color.FromArgb(100,220,0),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding); }
        }
    }

}
