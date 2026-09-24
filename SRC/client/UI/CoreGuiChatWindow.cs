using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using RobloxChatLauncher.Utils;

namespace RobloxChatLauncher.UI;

// Independent per-pixel-alpha surface. Historical themes do not render the modern
// panel, RichEdit control, rounded input, reset button, or resize grip.
internal sealed class CoreGuiChatWindow : Form
{
    private int scroll;
    private int scrollTarget;
    private long lastScrollTick = Environment.TickCount64;
    private Rectangle messageBounds;
    private Rectangle inputBounds;
    private Size layoutViewportSize;
    private string previousState = "";
    private static readonly Image? teamIcon = RobloxIconLoader.LoadIcon("ui/chat_teamButton.png");
    public event Action? InputClicked;
    public event Action<string>? SenderClicked;
    public event Action<Point, Point>? PositionChanged;
    internal bool DragEnabled;
    internal Point MessageOffset;
    internal Point InputOffset;
    private Point downScreen, downMessageOffset, downInputOffset;
    private bool pressed, moved, draggingInput;
    private string currentTheme = "2018";
    internal Size FrameSize;
    internal bool DarkText, Docked;
    internal event Action? Resized;
    internal Rectangle InputScreenBounds => new(Left + inputBounds.X, Top + inputBounds.Y, inputBounds.Width, inputBounds.Height);
    internal Rectangle MessageScreenBounds => new(Left + messageBounds.X, Top + messageBounds.Y, messageBounds.Width, messageBounds.Height);
    private Rectangle resizeBounds;
    private bool resizing;
    private Size downSize;
    private bool currentEditing;
    private List<(RectangleF Bounds,string Sender)> senderHits=new();
    public CoreGuiChatWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80 | 0x08000000; return cp; }
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; } // MA_NOACTIVATE
        if (m.Msg == 0x84)
        {
            long packed = m.LParam.ToInt64();
            Point point = PointToClient(new Point((short)packed, (short)(packed >> 16)));
            m.Result = (IntPtr)(inputBounds.Contains(point) || messageBounds.Contains(point) || resizeBounds.Contains(point) ? 1 : -1);
            return;
        }
        if (m.Msg == 0x20) { Cursor.Current = Cursors.Arrow; m.Result = (IntPtr)1; return; }
        base.WndProc(ref m);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            BeginPointer(Cursor.Position, e.Location);
            if (pressed) Capture = true;
        }
        base.OnMouseDown(e);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        MovePointer(Cursor.Position);
        base.OnMouseMove(e);
    }
    internal void BeginPointer(Point screen, Point local)
    {
        draggingInput = inputBounds.Contains(local);
        resizing = resizeBounds.Contains(local);
        pressed = resizing || draggingInput || messageBounds.Contains(local);
        downSize = messageBounds.Size;
        moved = false; downScreen = screen;
        downMessageOffset = MessageOffset; downInputOffset = InputOffset;
    }
    internal void MovePointer(Point screen)
    {
        if (pressed && (DragEnabled || resizing))
        {
            Point delta = new(screen.X - downScreen.X, screen.Y - downScreen.Y);
            if (moved || Math.Abs(delta.X) >= SystemInformation.DragSize.Width || Math.Abs(delta.Y) >= SystemInformation.DragSize.Height)
            {
                moved = true;
                if (resizing)
                    FrameSize = new Size(Math.Clamp(downSize.Width + delta.X, 180, Math.Max(180, layoutViewportSize.Width - messageBounds.X)),
                        Math.Clamp(downSize.Height + delta.Y, 60, Math.Max(60, layoutViewportSize.Height - messageBounds.Y - 80)));
                else if (currentTheme == "2014" && draggingInput && !Docked)
                    InputOffset = new Point(downInputOffset.X + delta.X, downInputOffset.Y + delta.Y);
                else MessageOffset = new Point(downMessageOffset.X + delta.X, downMessageOffset.Y + delta.Y);
                ConfigureInteraction(currentTheme, Size, currentEditing);
                previousState = "";
            }
        }
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && pressed)
        {
            EndPointer();
            Capture = false;
        }
        base.OnMouseUp(e);
    }
    internal void EndPointer()
    {
        if (!pressed) return;
        if (moved && resizing) Resized?.Invoke();
        else if (moved) PositionChanged?.Invoke(MessageOffset, InputOffset);
        else if (draggingInput) InputClicked?.Invoke();
        else
        {
            var hit=senderHits.LastOrDefault(h=>h.Bounds.Contains(PointToClient(Cursor.Position)));
            if(!string.IsNullOrWhiteSpace(hit.Sender))SenderClicked?.Invoke(hit.Sender);
        }
        pressed = false;
    }
    internal void ConfigureInteraction(string theme, Size viewport, bool editing)
    {
        currentTheme = theme; currentEditing = editing; layoutViewportSize = viewport;
        var baseline = GetLayout(theme, viewport, editing, default, default, FrameSize, Docked);
        (messageBounds, inputBounds) = GetLayout(theme, viewport, editing, MessageOffset, InputOffset, FrameSize, Docked);
        MessageOffset = new Point(messageBounds.X - baseline.Messages.X, messageBounds.Y - baseline.Messages.Y);
        if (theme == "2014") InputOffset = new Point(0, inputBounds.Y - baseline.Input.Y);
        resizeBounds = new Rectangle(messageBounds.Right - 14, messageBounds.Bottom - 14, 14, 14);
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        if (!Capture) pressed = false;
        base.OnMouseCaptureChanged(e);
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        scrollTarget = Math.Max(0, scrollTarget + (e.Delta > 0 ? 54 : -54));
        if (currentTheme == "2014") scroll = scrollTarget;
        previousState = "";
        base.OnMouseWheel(e);
    }

    // CoreGui's ScreenGui viewport excludes the 36px top bar in the newer chat.
    internal static (Rectangle Messages, Rectangle Input) GetLayout(string theme, Size viewport, bool editing = false,
        Point offset = default, Point inputOffset = default, Size frameSize = default, bool docked = false)
    {
        if (theme == "2014")
        {
            var message = new Rectangle(0, 5, viewport.Height < 600 ? 280 : 500, 120);
            if (!frameSize.IsEmpty) message.Size = new Size(Math.Clamp(frameSize.Width, 180, Math.Max(180, viewport.Width)), Math.Clamp(frameSize.Height, 60, Math.Max(60, viewport.Height - 100)));
            int barHeight = docked ? 32 : 20;
            var input = new Rectangle(0, Math.Max(0, viewport.Height - barHeight), viewport.Width, barHeight);
            message.Offset(Math.Clamp(offset.X, 0, Math.Max(0, viewport.Width - message.Width)),
                Math.Clamp(offset.Y, -message.Top, Math.Max(-message.Top, viewport.Height - message.Bottom - barHeight)));
            if (!docked) input.Offset(0, Math.Clamp(inputOffset.Y, -input.Top, 0));
            return (message, input);
        }
        int width = Math.Max(1, (int)Math.Round(viewport.Width * .3));
        int height = Math.Max(46, (int)Math.Round(Math.Max(1, viewport.Height - 36) * .25) + 24);
        var messages = new Rectangle(0, 38, width, height - 46);
        var bar = new Rectangle(0, 36 + height - 42, width, 42);
        if (theme == "2016")
        {
            double scaleX = viewport.Width <= 640 ? .5 : viewport.Width <= 1024 ? .4 : .3;
            double scaleY = viewport.Width <= 640 ? .5 : viewport.Width <= 1024 ? .3 : .25;
            width = (int)Math.Round(viewport.Width * scaleX);
            height = (int)Math.Round(Math.Max(1, viewport.Height - 36) * scaleY);
            messages = new Rectangle(0, 38, width, Math.Max(1, height - 2));
            bar = new Rectangle(0, 38 + height, width, editing ? 40 : 32);
        }
        if (!frameSize.IsEmpty)
        {
            messages.Size = new Size(Math.Clamp(frameSize.Width, 180, Math.Max(180, viewport.Width)), Math.Clamp(frameSize.Height, 60, Math.Max(60, viewport.Height - 120)));
            bar.Width = messages.Width; bar.Y = messages.Bottom + 2;
            width = messages.Width;
        }
        int x = Math.Clamp(offset.X, 0, Math.Max(0, viewport.Width - width));
        int y = Math.Clamp(offset.Y, -38, Math.Max(-38, viewport.Height - bar.Bottom));
        messages.Offset(x, y); bar.Offset(x, y);
        return (messages, bar);
    }

    public void RefreshSurface(string theme, Rectangle viewport, IReadOnlyList<RichChatBox.ChatLine> lines,
        string input, int caret, bool editing, bool shown)
    {
        if (!shown) { Hide(); return; }

        long now = Environment.TickCount64;
        scroll = SmoothScroll(scroll, scrollTarget, now - lastScrollTick);
        lastScrollTick = now;
        bool blink = editing && Environment.TickCount64 % 1000 < 500;
        string key = $"{theme}|{viewport}|{lines.Count}|{lines.LastOrDefault()}|{input}|{caret}|{editing}|{blink}|{scroll}|{MessageOffset}|{InputOffset}|{FrameSize}|{DarkText}|{Docked}";
        if (key == previousState && Visible) return;
        previousState = key;
        ConfigureInteraction(theme, viewport.Size, editing);
        Rectangle surface = theme == "2016" ? Rectangle.Union(messageBounds, inputBounds) : new Rectangle(Point.Empty, viewport.Size);
        int requestedScroll = scroll;
        var nextSenderHits=new List<(RectangleF Bounds,string Sender)>();
        using Bitmap bitmap = DrawSurface(theme, viewport.Size, lines, input, caret, blink, editing, ref scroll, MessageOffset, InputOffset, FrameSize, DarkText, Docked,nextSenderHits,surface);
        senderHits=nextSenderHits;
        if(theme=="2016")
        {
            messageBounds.Offset(-surface.X,-surface.Y);inputBounds.Offset(-surface.X,-surface.Y);resizeBounds.Offset(-surface.X,-surface.Y);
            for(int i=0;i<senderHits.Count;i++) senderHits[i]=(new RectangleF(senderHits[i].Bounds.X-surface.X,senderHits[i].Bounds.Y-surface.Y,senderHits[i].Bounds.Width,senderHits[i].Bounds.Height),senderHits[i].Sender);
        }
        Bounds=new Rectangle(viewport.X+surface.X,viewport.Y+surface.Y,surface.Width,surface.Height);
        if (scroll < requestedScroll) scrollTarget = scroll;
        Present(bitmap);
        if (!Visible) Show();
    }

    internal static Bitmap DrawSurface(string theme, Size size, IReadOnlyList<RichChatBox.ChatLine> lines,
        string input, int caret, bool blink, bool editing, ref int scroll, Point offset = default, Point inputOffset = default,
        Size frameSize = default, bool darkText = false, bool docked = false,List<(RectangleF Bounds,string Sender)>? senderHits=null,Rectangle? surface=null)
    {
        Rectangle drawingArea=surface??new Rectangle(Point.Empty,size);
        var bitmap = new Bitmap(Math.Max(1, drawingArea.Width), Math.Max(1, drawingArea.Height), PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        g.TranslateTransform(-drawingArea.X,-drawingArea.Y);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var (messages, bar) = GetLayout(theme, size, editing, offset, inputOffset, frameSize, docked);
        bool old = theme == "2014";
        bool legacy = theme == "2016";
        using Font font = RichChatBox.GetThemeFont(bold: !old);
        using Font inputFont = old ? new Font("Arial", 12, FontStyle.Regular, GraphicsUnit.Pixel) : RichChatBox.GetThemeFont(bold: true, input: true);
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;

        if (old)
        {
            using var background = new SolidBrush(Color.FromArgb(102, 0, 0, 0));
            g.FillRectangle(background, messages);
        }
        else
        {
            using var background = new SolidBrush(legacy ? Color.FromArgb(128, 31, 31, 31) : Color.FromArgb(102, 0, 0, 0));
            g.FillRectangle(background, messages);
            g.FillRectangle(background, bar);
        }
        // Keep alpha steady while reproducing the archived layer colors.
        Rectangle box = old ? bar : Rectangle.Inflate(bar, -7, legacy ? -5 : -7);
        using (var brush = new SolidBrush(old ? Color.FromArgb(191, 0, 0, 0) : legacy ? Color.FromArgb(128, 209, 216, 221) : Color.FromArgb(102, 255, 255, 255))) g.FillRectangle(brush, box);
        Rectangle textRect = old ? box : legacy ? Rectangle.Inflate(box, -7, 0) : Rectangle.Inflate(box, -5, -5);
        string placeholder = old ? "To chat click here or press \"/\" key" : "To chat click here or press '/' key";
        string shown = !editing && input.Length == 0 ? placeholder : input;
        Color inputColor = old ? (editing ? Color.White : Color.FromArgb(255, 255, 230)) : legacy ? Color.FromArgb(112, 110, 106) : Color.FromArgb(153, 0, 0, 0);
        var state = g.Save();
        g.SetClip(textRect);
        using (var brush = new SolidBrush(inputColor))
        {
            float inputX = textRect.X;
            foreach (string part in EmojiPicker.Runs(shown))
            {
                if (EmojiImages.Draw(g, part, new RectangleF(inputX, textRect.Y + (textRect.Height - 18) / 2f, 18, 18)))
                { inputX += 18; continue; }
                using Font chosen = EmojiPicker.IsEmoji(part) ? new Font("Segoe UI Emoji", old ? 12 : 18, FontStyle.Regular, GraphicsUnit.Pixel) : (Font)inputFont.Clone();
                using var inputPath = new GraphicsPath();
                inputPath.AddString(part, chosen.FontFamily, (int)chosen.Style, old ? 12 : 18,
                    new PointF(inputX, legacy ? textRect.Y + (textRect.Height - 22) / 2f : textRect.Y - (old ? 0 : 2) + (docked ? 6 : 0)), format);
                g.FillPath(brush, inputPath);
                inputX += g.MeasureString(part, chosen, int.MaxValue, format).Width;
            }
            if (blink)
            {
                float advance = EmojiPicker.Runs(input[..Math.Clamp(caret, 0, input.Length)])
                    .Sum(part => EmojiImages.HasImage(part) ? 18f : g.MeasureString(part, inputFont, int.MaxValue, format).Width);
                using var pen = new Pen(inputColor);
                g.DrawLine(pen, textRect.X + advance, textRect.Y, textRect.X + advance, textRect.Y + 17);
            }
        }
        g.Restore(state);

        // Each Roblox message is a separate text label with no RichEdit margins.
        int inset = old ? (int)Math.Round(messages.Width * .025) + 20 : legacy ? 10 : 8;
        int lineHeight = legacy ? 19 : 18;
        float available = Math.Max(1, messages.Width - inset - 10);
        var rows = new List<List<(string Text, Color Color,string? Sender)>>();
        var icons = new Dictionary<int, Color>();
        foreach (var line in lines.TakeLast(old ? 20 : 50))
        {
            if (old && line.Prefix.Length > 0) icons[rows.Count] = line.Color;
            var row = new List<(string, Color,string?)>();
            float used = 0;
            string prefix = !old && line.Prefix.EndsWith(": ") && !line.Prefix.StartsWith('[')
                ? $"[{line.Prefix[..^2]}]: " : line.Prefix;
            foreach (var run in new[] { (prefix, old ? Color.White : line.Color,line.ClickSender), (line.Text, legacy ? Color.FromArgb(255, 255, 243) : Color.White,(string?)null) })
            {
                foreach (string token in System.Text.RegularExpressions.Regex.Split(run.Item1.Replace("\r", ""), "(\\s+)").SelectMany(EmojiPicker.Runs))
                {
                    if (token.Length == 0) continue;
                    float width = MeasureRun(g, token, font, format);
                    if (token.Contains('\n') || (used + width > available && used > 0))
                    {
                        rows.Add(row); row = new(); used = 0;
                        if (string.IsNullOrWhiteSpace(token)) continue;
                    }
                    // Split long unbroken input so it cannot overflow the chat frame.
                    if (width > available)
                    {
                        foreach (string letter in System.Globalization.StringInfo.GetTextElementEnumerator(token).AsElements())
                        {
                            float w = g.MeasureString(letter, font, int.MaxValue, format).Width;
                            if (used + w > available && used > 0) { rows.Add(row); row = new(); used = 0; }
                            row.Add((letter, run.Item2,run.Item3)); used += w;
                        }
                    }
                    else { row.Add((token, run.Item2,run.Item3)); used += width; }
                }
            }
            rows.Add(row);
        }
        Rectangle clip = old ? messages : legacy ? Rectangle.FromLTRB(messages.Left, messages.Top + 10, messages.Right - 4, messages.Bottom - 10) : Rectangle.FromLTRB(messages.Left, messages.Top + 3, messages.Right - 4, messages.Bottom - 3);
        int contentHeight = rows.Count * lineHeight;
        int overflow = Math.Max(0, contentHeight - clip.Height);
        scroll = Math.Min(scroll, overflow);
        float y = old || legacy ? clip.Bottom - contentHeight + scroll : clip.Top - overflow + scroll;
        state = g.Save();
        g.SetClip(clip);
        int rowIndex = 0;
        foreach (var row in rows)
        {
            float x = messages.X + inset;
            if (old && teamIcon != null && icons.TryGetValue(rowIndex, out Color iconColor))
            {
                using var attributes = new ImageAttributes();
                attributes.SetColorMatrix(new ColorMatrix
                {
                    Matrix00 = iconColor.R / 255f, Matrix11 = iconColor.G / 255f,
                    Matrix22 = iconColor.B / 255f, Matrix33 = 1, Matrix44 = 1
                });
                g.DrawImage(teamIcon, new Rectangle(messages.X + inset - 20, (int)y + 4, 14, 14),
                    0, 0, teamIcon.Width, teamIcon.Height, GraphicsUnit.Pixel, attributes);
            }
            foreach (var run in row)
            {
                float runWidth=MeasureRun(g,run.Text,font,format);
                if(run.Sender!=null&&y+lineHeight>=clip.Top&&y<clip.Bottom)
                    senderHits?.Add((new RectangleF(x,y,runWidth,lineHeight),run.Sender));
                if (EmojiImages.HasImage(run.Text))
                {
                    if (y + lineHeight >= clip.Top && y < clip.Bottom) EmojiImages.Draw(g, run.Text, new RectangleF(x, y, 18, 18));
                    x += 18; continue;
                }
                if (y + lineHeight >= clip.Top && y < clip.Bottom)
                {
                    using var path = new GraphicsPath();
                    using Font chosen = EmojiPicker.IsEmoji(run.Text) ? new Font("Segoe UI Emoji", 18, FontStyle.Regular, GraphicsUnit.Pixel) : (Font)font.Clone();
                    path.AddString(run.Text, chosen.FontFamily, (int)chosen.Style, 18, new PointF(x, y), format);
                    using var outline = new Pen(darkText ? Color.FromArgb(110, 255, 255, 255) : Color.FromArgb(old ? 102 : 64, 0, 0, 0), 1.5f) { LineJoin = LineJoin.Round };
                    g.DrawPath(outline, path);
                    using var brush = new SolidBrush(darkText ? Color.FromArgb(20, 20, 20) : run.Color);
                    g.FillPath(brush, path);
                }
                x += runWidth;
            }
            y += lineHeight;
            rowIndex++;
        }
        g.Restore(state);
        if (!old && overflow > 0)
        {
            float thumb = Math.Max(10, clip.Height * clip.Height / (float)contentHeight);
            float top = clip.Top + (clip.Height - thumb) * (overflow - scroll) / overflow;
            using var brush = new SolidBrush(Color.FromArgb(180, 255, 255, 255));
            g.FillRectangle(brush, messages.Right - 8, top, 4, thumb);
        }
        using (var grip = new Pen(Color.FromArgb(210, 180, 180, 180), 2))
            for (int i = 3; i <= 11; i += 4) g.DrawLine(grip, messages.Right - i, messages.Bottom - 2, messages.Right - 2, messages.Bottom - i);
        return bitmap;
    }

    internal static int SmoothScroll(int current, int target, long elapsed)
    {
        if (current == target) return current;
        double amount = 1 - Math.Exp(-Math.Clamp(elapsed, 1, 100) / 65.0);
        int next = (int)Math.Round(current + (target - current) * amount);
        return next == current ? current + Math.Sign(target - current) : next;
    }

    private static float MeasureRun(Graphics g, string text, Font font, StringFormat format)
    {
        if (!EmojiPicker.IsEmoji(text)) return g.MeasureString(text, font, int.MaxValue, format).Width;
        if (EmojiImages.HasImage(text)) return 18;
        using var emojiFont = new Font("Segoe UI Emoji", 18, FontStyle.Regular, GraphicsUnit.Pixel);
        return Math.Max(18, g.MeasureString(text, emojiFont, int.MaxValue, format).Width);
    }

    internal void Present(Bitmap bitmap)
    {
        IntPtr screen = GetDC(IntPtr.Zero), memory = CreateCompatibleDC(screen);
        IntPtr image = bitmap.GetHbitmap(Color.FromArgb(0)), previous = SelectObject(memory, image);
        try
        {
            var destination = new Point(Left, Top); var source = Point.Empty; var size = bitmap.Size;
            var blend = new Blend { Alpha = 255, Format = 1 };
            if (!UpdateLayeredWindow(Handle, screen, ref destination, ref size, memory, ref source, 0, ref blend, 2))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { SelectObject(memory, previous); DeleteObject(image); DeleteDC(memory); ReleaseDC(IntPtr.Zero, screen); }
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { public byte Operation, Flags, Alpha, Format; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr dc, ref Point destination, ref Size size, IntPtr sourceDc, ref Point source, int key, ref Blend blend, int flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
