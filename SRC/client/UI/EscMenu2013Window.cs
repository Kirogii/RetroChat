using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RobloxChatLauncher.UI;

// One per-pixel-alpha surface keeps the live game visible through the panel while
// button pixels remain fully opaque and receive input directly.
internal sealed class EscMenu2013Window : Form
{
    private enum Page { Main,Reset,Leave }
    private sealed record MenuItem(string Text,Rectangle Bounds,string Action,bool Primary=false,string? Shortcut=null);
    private Page page;
    private int hovered=-1;
    private readonly System.Windows.Forms.Timer tweenTimer=new() { Interval=33 };
    private long tweenStarted;
    private int tweenFromY;
    private int tweenY;
    internal event Action<string>? ActionRequested;
    internal event Action? Dismissed;
    internal string CurrentPage=>page.ToString();
    protected override bool ShowWithoutActivation=>true;
    protected override CreateParams CreateParams { get { var cp=base.CreateParams;cp.ExStyle|=0x80000|0x80|0x08000000;return cp; } }
    internal EscMenu2013Window() { FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;AutoScaleMode=AutoScaleMode.None;TopMost=true;tweenTimer.Tick+=(_,_)=>TweenTick(); }
    private Rectangle PanelBounds=>new((Width-492)/2,(Height-459)/2+tweenY,492,459);
    private IReadOnlyList<MenuItem> Items()
    {
        Rectangle p=PanelBounds;
        Rectangle R(int x,int y,int w,int h)=>new(p.X+x,p.Y+y,w,h);
        if(page==Page.Reset)return new[]{new MenuItem("cancel",R(48,348,182,43),"cancel"),new MenuItem("Reset",R(263,348,182,43),"reset-confirm",true)};
        if(page==Page.Leave)return new[]{new MenuItem("No",R(48,348,182,43),"cancel"),new MenuItem("Yes",R(263,348,182,43),"leave-confirm",true)};
        return new[]{
            new MenuItem("Resume Game",R(80,61,334,43),"resume",true),new MenuItem("Reset Character",R(80,118,334,43),"reset"),
            new MenuItem("Game Setting",R(80,174,334,43),"settings"),new MenuItem("Help",R(80,258,161,43),"help"),
            new MenuItem("Screenshot",R(251,258,163,43),"screenshot",false,"PrintSc"),new MenuItem("Record Video",R(251,314,163,42),"record",false,"F12"),
            new MenuItem("Leave Game",R(80,379,334,43),"leave")};
    }
    internal void Open(Rectangle bounds) { Bounds=bounds;page=Page.Main;hovered=-1;tweenY=-Math.Max(459,Height/2);Render();if(!Visible)Show();BringToFront();StartTween(tweenY); }
    internal void ShowReset() { AnimatePage(Page.Reset,Height); }
    internal void CloseMenu() { if(Visible)Hide();Dismissed?.Invoke(); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e);int next=Items().Select((v,i)=>(v,i)).FirstOrDefault(x=>x.v.Bounds.Contains(e.Location),defaultValue:(null!,-1)).i;if(next!=hovered){hovered=next;Render();} }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e);if(hovered!=-1){hovered=-1;Render();} }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);if(e.Button!=MouseButtons.Left)return;
        var item=Items().FirstOrDefault(x=>x.Bounds.Contains(e.Location));if(item==null)return;Activate(item);
    }
    private void Activate(MenuItem item)
    {
        if(item.Action=="reset"){AnimatePage(Page.Reset,Height);return;}
        if(item.Action=="leave"){AnimatePage(Page.Leave,-Height);return;}
        if(item.Action=="cancel"){AnimatePage(Page.Main,page==Page.Reset?-Height:Height);return;}
        CloseMenu();ActionRequested?.Invoke(item.Action);
    }
    internal void ClickForTest(string action)
    {
        if(action=="reset"){page=Page.Reset;tweenY=0;return;}
        if(action=="leave"){page=Page.Leave;tweenY=0;return;}
        if(action=="cancel"){page=Page.Main;tweenY=0;return;}
        var item=Items().First(x=>x.Action==action);Activate(item);
    }
    private void AnimatePage(Page next,int fromY){page=next;hovered=-1;tweenY=fromY;StartTween(fromY);}
    private void StartTween(int fromY){tweenFromY=fromY;tweenStarted=Environment.TickCount64;tweenTimer.Start();Render();}
    private void TweenTick()
    {
        double t=Math.Clamp((Environment.TickCount64-tweenStarted)/200.0,0,1);
        double eased=.5-.5*Math.Cos(Math.PI*t);
        int next=(int)Math.Round(tweenFromY*(1-eased));
        if(next!=tweenY){tweenY=next;Render();}
        if(t>=1){tweenY=0;tweenTimer.Stop();}
    }
    internal Bitmap CreateBitmap()
    {
        var bitmap=new Bitmap(Math.Max(1,Width),Math.Max(1,Height),PixelFormat.Format32bppPArgb);using var g=Graphics.FromImage(bitmap);g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var dim=new SolidBrush(Color.FromArgb(107,38,40,42)))g.FillRectangle(dim,0,0,Width,Height);
        Rectangle p=PanelBounds;using(var path=Rounded(p,12))using(var panel=new SolidBrush(Color.FromArgb(222,17,18,19)))g.FillPath(panel,path);
        using var titleFont=new Font("Arial",26,FontStyle.Bold,GraphicsUnit.Pixel);using var bodyFont=new Font("Arial",20,FontStyle.Regular,GraphicsUnit.Pixel);using var smallFont=new Font("Arial",13,FontStyle.Regular,GraphicsUnit.Pixel);
        if(page==Page.Main)TextRenderer.DrawText(g,"Game Menu",titleFont,new Rectangle(p.X,p.Y+9,p.Width,35),Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
        else {
            string title=page==Page.Reset?"Are you sure to reset your charater?":"Are you sure you want to leave this game?";
            string sub=page==Page.Reset?"You will be put back on a spawn point":"You will leave the current Roblox game";
            TextRenderer.DrawText(g,title,titleFont,new Rectangle(p.X+15,p.Y+99,p.Width-30,40),Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g,sub,smallFont,new Rectangle(p.X+19,p.Y+191,p.Width-38,30),Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
        }
        var items=Items();for(int i=0;i<items.Count;i++)DrawButton(g,items[i],i==hovered,bodyFont,smallFont);
        return bitmap;
    }
    private static void DrawButton(Graphics g,MenuItem item,bool hover,Font font,Font small)
    {
        using var shape=Rounded(Rectangle.Inflate(item.Bounds,-1,-1),5);using var fill=new SolidBrush(Color.FromArgb(255,1,1,1));g.FillPath(fill,shape);
        using var pen=new Pen(hover?Color.White:item.Primary?Color.Red:Color.FromArgb(75,77,78),hover||item.Primary?2:1);g.DrawPath(pen,shape);
        Rectangle text=item.Bounds;if(item.Shortcut!=null)text.Width-=25;
        TextRenderer.DrawText(g,item.Text,item.Bounds.Width<200?small:font,text,Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
        if(item.Shortcut!=null) { using var shortcutFont=new Font("Arial",6,GraphicsUnit.Pixel);TextRenderer.DrawText(g,item.Shortcut,shortcutFont,new Rectangle(item.Bounds.Right-39,item.Bounds.Y,35,item.Bounds.Height),Color.LimeGreen,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding); }
    }
    private static GraphicsPath Rounded(Rectangle r,int radius) { var path=new GraphicsPath();int d=radius*2;path.AddArc(r.X,r.Y,d,d,180,90);path.AddArc(r.Right-d,r.Y,d,d,270,90);path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.X,r.Bottom-d,d,d,90,90);path.CloseFigure();return path; }
    internal void Render()
    {
        using var bitmap=CreateBitmap();IntPtr screen=GetDC(IntPtr.Zero),memory=CreateCompatibleDC(screen),image=bitmap.GetHbitmap(Color.FromArgb(0)),previous=SelectObject(memory,image);
        try { var destination=Location;var source=Point.Empty;var size=bitmap.Size;var blend=new Blend{Alpha=255,Format=1};if(!UpdateLayeredWindow(Handle,screen,ref destination,ref size,memory,ref source,0,ref blend,2))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
        finally { SelectObject(memory,previous);DeleteObject(image);DeleteDC(memory);ReleaseDC(IntPtr.Zero,screen); }
    }
    protected override void Dispose(bool disposing){if(disposing)tweenTimer.Dispose();base.Dispose(disposing);}
    [StructLayout(LayoutKind.Sequential,Pack=1)]private struct Blend { public byte Operation,Flags,Alpha,Format; }
    [DllImport("user32.dll",SetLastError=true)]private static extern bool UpdateLayeredWindow(IntPtr window,IntPtr dc,ref Point destination,ref Size size,IntPtr sourceDc,ref Point source,int key,ref Blend blend,int flags);
    [DllImport("user32.dll")]private static extern IntPtr GetDC(IntPtr window);[DllImport("user32.dll")]private static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("gdi32.dll")]private static extern IntPtr CreateCompatibleDC(IntPtr dc);[DllImport("gdi32.dll")]private static extern IntPtr SelectObject(IntPtr dc,IntPtr value);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(IntPtr value);[DllImport("gdi32.dll")]private static extern bool DeleteDC(IntPtr dc);
}
