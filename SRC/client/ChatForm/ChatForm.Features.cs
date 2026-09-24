using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using RobloxChatLauncher.UI;
using RobloxChatLauncher.Utils;

namespace RobloxChatLauncher;

public partial class ChatForm
{
    private EmojiPicker? emojiPicker;
    private Dictionary<string, Size> frameSizes = new();
    private bool DarkText => Properties.Settings1.Default.ChatTextStyle == "dark";
    private bool Dock2014 => activeTheme == "2014" && Properties.Settings1.Default.Theme2014Mode == "docked";
    private Placement? originalPlacement;
    private Rectangle lastDockArea;

    private void SaveHistoricalLayout()
    {
        if (coreGuiWindow == null) return;
        dragOffsets[activeTheme] = coreGuiWindow.MessageOffset;
        dragOffsets["2014-input"] = coreGuiWindow.InputOffset;
        frameSizes[activeTheme] = coreGuiWindow.FrameSize;
        Properties.Settings1.Default.ChatDragOffsets = JsonSerializer.Serialize(dragOffsets);
        Properties.Settings1.Default.ChatFrameSizes = JsonSerializer.Serialize(frameSizes);
        Properties.Settings1.Default.Save();
    }
    private bool HandleTextCommand(string args)
    {
        string mode = args.Trim().ToLowerInvariant();
        if (mode is not ("dark" or "light")) { RichChatBox.AppendSystemMessage(chatBox, "Usage: /text <dark|light>"); return true; }
        Properties.Settings1.Default.ChatTextStyle = mode;
        Properties.Settings1.Default.Save();
        ApplyTextStyle();
        RichChatBox.AppendSystemMessage(chatBox, $"Text colour: {mode}.");
        return true;
    }
    private void ApplyTextStyle()
    {
        chatBox.ForeColor = DarkText ? Color.FromArgb(20, 20, 20) : Color.White;
        if (activeTheme == "default")
        {
            mainContainer.BackColor = chatBox.BackColor = DarkText ? Color.FromArgb(220, 228, 235) : Color.FromArgb(35, 45, 55);
            inputBox.ForeColor = DarkText ? Color.Black : Color.White;
            inputBox.BackColor = DarkText ? Color.FromArgb(240, 243, 246) : Color.FromArgb(25, 25, 25);
        }
        chatBox.SelectAll(); chatBox.SelectionColor = chatBox.ForeColor; chatBox.Select(chatBox.TextLength, 0);
        RefreshCoreGui();
    }
    internal bool TryEmojiKey(Keys key) => emojiPicker?.HandleKey(key) == true;
    private void UpdateEmojiSuggestions()
    {
        if (inputBox == null) return;
        if (emojiPicker == null)
        {
            emojiPicker = new EmojiPicker { Owner = this };
            emojiPicker.Chosen += emoji =>
            {
                var token = EmojiPicker.Token(rawInputText, inputBox.CaretIndex);
                if (!token.Success) return;
                rawInputText = rawInputText.Remove(token.Index, token.Length).Insert(token.Index, emoji);
                inputBox.CaretIndex = token.Index + emoji.Length;
                SyncInput();
            };
        }
        Rectangle bar = activeTheme == "default" ? inputBox.RectangleToScreen(inputBox.ClientRectangle)
            : coreGuiWindow?.InputScreenBounds ?? Rectangle.Empty;
        emojiPicker.TopMost = IsRobloxForegroundProcess();
        emojiPicker.UpdateSuggestions(rawInputText, inputBox.CaretIndex, bar, isChatting && !isWindowHidden && IsRobloxForegroundProcess());
    }
    private Rectangle HistoricalViewport()
    {
        IntPtr window = robloxProcess.MainWindowHandle;
        if (Dock2014 && !isWindowHidden)
        {
            var work = Screen.FromHandle(window).WorkingArea;
            if (originalPlacement == null)
            {
                var placement = new Placement { Length = Marshal.SizeOf<Placement>() };
                if (GetWindowPlacement(window, ref placement)) originalPlacement = placement;
            }
            if (lastDockArea != work && originalPlacement != null)
            {
                lastDockArea = work;
                ShowWindow(window, 9); // Restore before fitting into the taskbar-safe work area.
                SetWindowPos(window, IntPtr.Zero, work.X, work.Y, work.Width, Math.Max(100, work.Height - 32), 0x14);
            }
        }
        else RestoreGamePlacement();
        NativeMethods.GetClientRect(window, out var client);
        Point origin = Point.Empty; NativeMethods.ClientToScreen(window, ref origin);
        int height = client.Bottom;
        if (Dock2014 && !isWindowHidden && NativeMethods.GetWindowRect(window, out var outer)) height = outer.Bottom - origin.Y + 32;
        return new Rectangle(origin, new Size(client.Right, Math.Max(1, height)));
    }
    private void RestoreGamePlacement()
    {
        if (originalPlacement is not { } placement) return;
        originalPlacement = null; lastDockArea = Rectangle.Empty;
        try { if (!robloxProcess.HasExited) SetWindowPlacement(robloxProcess.MainWindowHandle, ref placement); }
        catch (InvalidOperationException) { }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Placement
    {
        public int Length, Flags, Show;
        public Point Minimum, Maximum;
        public NativeMethods.RECT Normal;
    }
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr window, ref Placement placement);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(IntPtr window, ref Placement placement);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
