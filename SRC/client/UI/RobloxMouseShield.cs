using System.Drawing;
using System.Windows.Forms;
using RobloxChatLauncher.Utils;

namespace RobloxChatLauncher.UI;

// A non-activating surface receives pointer messages while Roblox keeps keyboard
// focus. Nonzero opacity is essential: fully transparent windows pass input through.
internal sealed class RobloxMouseShield : Form
{
    private readonly IntPtr target;
    private readonly System.Windows.Forms.Timer tracker=new(){Interval=30};
    internal RobloxMouseShield(IntPtr target)
    {
        this.target=target;
        FormBorderStyle=FormBorderStyle.None;
        ShowInTaskbar=false;
        StartPosition=FormStartPosition.Manual;
        BackColor=Color.Black;
        Opacity=1.0/255;
        TopMost=true;
        tracker.Tick+=(_,_)=>Track();
        Track();
        tracker.Start();
    }
    protected override bool ShowWithoutActivation=>true;
    protected override CreateParams CreateParams
    {
        get { var cp=base.CreateParams;cp.ExStyle|=0x08000000|0x00000080;return cp; }
    }
    private void Track()
    {
        if(NativeMethods.GetForegroundWindow()!=target||NativeMethods.IsIconic(target))
        { Hide();return; }
        if(!NativeMethods.GetClientRect(target,out var rect))return;
        Point origin=Point.Empty;
        if(!NativeMethods.ClientToScreen(target,ref origin))return;
        Bounds=new Rectangle(origin,new Size(rect.Right,rect.Bottom));
        if(!Visible)Show();
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x0021){m.Result=(IntPtr)3;return;} // MA_NOACTIVATE
        if(m.Msg>=0x0200&&m.Msg<=0x020E){m.Result=IntPtr.Zero;return;}
        base.WndProc(ref m);
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing)tracker.Dispose();
        base.Dispose(disposing);
    }
}
