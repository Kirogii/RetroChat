using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RobloxChatLauncher.UI;

// No opaque Panel/ListBox backing: preserve the original PNG alpha all the way to DWM.
internal sealed class TextureRoster : Form
{
    internal ChatMember[] Members = Array.Empty<ChatMember>();
    internal event Action<int>? MemberClicked;
    internal event Action? LayoutChanged;
    private int firstRow;
    internal int FirstRow => firstRow;
    internal bool Maximized { get; private set; }
    internal bool Minimized { get; private set; }
    internal int RenderedRowHeight=>RowHeight;
    private long membersReceivedAt=Environment.TickCount64;
    private readonly System.Windows.Forms.Timer clock=new() { Interval=1000 };
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var cp=base.CreateParams;cp.ExStyle|=0x80000|0x80|0x08000000;return cp; } }
    internal TextureRoster() {
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;AutoScaleMode=AutoScaleMode.None;
        clock.Tick+=(_,_)=>{if(Visible&&Members.Length>0)Render();};clock.Start();
    }
    private float Scale=>Maximized?1.5f:1.65f;
    private int S(float value)=>(int)Math.Round(value*Scale);
    private int HeaderHeight=>S(57);
    private int RowHeight=>S(12);
    private int BottomHeight=>S(12);
    private int VisibleRows => Minimized?0:Math.Max(0,(Height-HeaderHeight-BottomHeight)/RowHeight);
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e);firstRow=Math.Clamp(firstRow-Math.Sign(e.Delta)*3,0,Math.Max(0,Members.Length-VisibleRows));Render(); }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if(e.Button!=MouseButtons.Left) return;
        if(e.Y>=Height-BottomHeight) { ToggleMinimized();return; }
        if(e.Y<HeaderHeight||e.Y>=HeaderHeight+VisibleRows*RowHeight) return;
        int index=firstRow+(e.Y-HeaderHeight)/RowHeight;
        if(index<Members.Length) MemberClicked?.Invoke(index);
    }
    internal void ToggleMaximized() { Maximized=!Maximized;Minimized=false;firstRow=0;LayoutChanged?.Invoke(); }
    private void ToggleMinimized() { Minimized=!Minimized;Maximized=false;firstRow=0;LayoutChanged?.Invoke(); }
    internal void UpdateMembers(ChatMember[] members)
    {
        long now=Environment.TickCount64;
        var previous=Members.ToDictionary(m=>m.Id,StringComparer.Ordinal);
        Members=members.Select(member=>previous.TryGetValue(member.Id,out var old)
            ? member with { SessionSeconds=Math.Max(member.SessionSeconds,LocallyAdvanced(old.SessionSeconds,membersReceivedAt,now)) }
            : member).ToArray();
        membersReceivedAt=now;
        if(Visible)Render();
    }
    internal Bitmap CreateBitmap()
    {
        var bitmap=new Bitmap(Math.Max(1,Width),Math.Max(1,Height),PixelFormat.Format32bppPArgb);
        using var g=Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        void Texture(string id,Rectangle bounds) { var img=CoreGuiTextures.Get(id);if(img!=null)g.DrawImage(img,bounds); }
        void Text(string text,Font font,Rectangle bounds,bool right=false) {
            using var format=new StringFormat { Alignment=right?StringAlignment.Far:StringAlignment.Near,LineAlignment=StringAlignment.Center,
                FormatFlags=StringFormatFlags.NoWrap,Trimming=StringTrimming.EllipsisCharacter };
            g.DrawString(text,font,Brushes.White,bounds,format);
        }
        using var heading=new Font("Arial",S(12),FontStyle.Bold,GraphicsUnit.Pixel);
        using var tiny=new Font("Arial",S(7),FontStyle.Bold,GraphicsUnit.Pixel);
        using var groupFont=new Font("Arial",S(9),FontStyle.Bold,GraphicsUnit.Pixel);
        using var rowFont=new Font("Arial",S(9),FontStyle.Bold,GraphicsUnit.Pixel);
        string header=Maximized?"96097470":"94692054", middleDark=Maximized?"96098866":"94691980",middleLight=Maximized?"96098920":"94692025";
        Texture(header,new Rectangle(0,0,Width,HeaderHeight));
        Text("Chatroom",heading,new Rectangle(S(4),S(2),Width-S(8),S(16)),true);
        Text(Members.Length.ToString(),heading,new Rectangle(S(4),S(18),Width-S(8),S(15)),true);
        Text("Session      Local Time",tiny,new Rectangle((int)(Width*.50),S(33),(int)(Width*.48),S(11)),true);
        Text("Chatroom Members",groupFont,new Rectangle(S(3),S(45),Width-S(6),S(12)));
        firstRow=Math.Clamp(firstRow,0,Math.Max(0,Members.Length-VisibleRows));
        using var badge=new SolidBrush(Color.FromArgb(70,180,245));
        for(int row=0;row<VisibleRows&&firstRow+row<Members.Length;row++) {
            int y=HeaderHeight+row*RowHeight;var member=Members[firstRow+row];
            Texture((firstRow+row)%2==0?middleDark:middleLight,new Rectangle(0,y,Width,RowHeight));
            g.FillRectangle(badge,S(3),y+S(3),S(7),S(5));
            g.FillPolygon(badge,new[]{new Point(S(4),y+S(7)),new Point(S(4),y+S(10)),new Point(S(7),y+S(7))});
            Text(member.Name,rowFont,new Rectangle(S(12),y,(int)(Width*.58)-S(12),RowHeight));
            long now=Environment.TickCount64;
            Text(Time(LocallyAdvanced(member.SessionSeconds,membersReceivedAt,now)),rowFont,new Rectangle((int)(Width*.6),y,(int)(Width*.18),RowHeight),true);
            string localTime=member.TimezoneOffsetMinutes is int offset
                ? DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(offset)).ToString("h:mmtt")
                : "--:--";
            Text(localTime,tiny,new Rectangle((int)(Width*.76),y,(int)(Width*.22)-S(2),RowHeight),true);
        }
        Texture(Maximized?"96397271":"94754966",new Rectangle(0,Height-BottomHeight,Width,BottomHeight));
        // PlayerList.lua: Position (.608, .3), Size (.3, .7) within BottomFrame.
        Texture(Minimized?"94692731":"94825585",new Rectangle(
            (int)Math.Round(Width*.608),Height-BottomHeight+(int)Math.Round(BottomHeight*.3),
            (int)Math.Round(Width*.3),(int)Math.Round(BottomHeight*.7)));
        return bitmap;
    }
    private static string Time(long seconds)=>$"{Math.Max(0,seconds)/60}:{Math.Max(0,seconds)%60:00}";
    internal static long LocallyAdvanced(long serverSeconds,long receivedAt,long now)=>serverSeconds+Math.Max(0,(now-receivedAt)/1000);
    internal void Render()
    {
        using var bitmap=CreateBitmap();
        IntPtr screen=GetDC(IntPtr.Zero),memory=CreateCompatibleDC(screen);
        IntPtr image=bitmap.GetHbitmap(Color.FromArgb(0)),previous=SelectObject(memory,image);
        try {
            var destination=Location;var source=Point.Empty;var size=bitmap.Size;
            var blend=new Blend { Alpha=255,Format=1 };
            if(!UpdateLayeredWindow(Handle,screen,ref destination,ref size,memory,ref source,0,ref blend,2))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        } finally { SelectObject(memory,previous);DeleteObject(image);DeleteDC(memory);ReleaseDC(IntPtr.Zero,screen); }
    }
    protected override void Dispose(bool disposing) { if(disposing)clock.Dispose();base.Dispose(disposing); }
    [StructLayout(LayoutKind.Sequential,Pack=1)] private struct Blend { public byte Operation,Flags,Alpha,Format; }
    [DllImport("user32.dll",SetLastError=true)] private static extern bool UpdateLayeredWindow(IntPtr window,IntPtr dc,ref Point destination,ref Size size,IntPtr sourceDc,ref Point source,int key,ref Blend blend,int flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc,IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
