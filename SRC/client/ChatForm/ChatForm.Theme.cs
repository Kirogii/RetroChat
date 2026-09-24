using System.Drawing;
using System.Windows.Forms;
using RobloxChatLauncher.UI;
using RobloxChatLauncher.Utils;

namespace RobloxChatLauncher;

public partial class ChatForm
{
    private IReadOnlyList<RichChatBox.ChatLine> cachedCoreGuiLines = Array.Empty<RichChatBox.ChatLine>();
    private int cachedCoreGuiTextLength = -1;
    private static readonly HashSet<string> ValidThemes = new(StringComparer.OrdinalIgnoreCase) { "default", "2014", "2016", "2018" };
    private string activeTheme = "default";
    private CoreGuiChatWindow? coreGuiWindow;
    internal bool IsChatMode => isChatting;
    private Dictionary<string, Point> dragOffsets = new();

    private bool HandleDragCommand(string args)
    {
        string value = args.Trim().ToLowerInvariant();
        if (value is not ("on" or "off"))
        {
            RichChatBox.AppendSystemMessage(chatBox, "Usage: /drag <on|off>");
            return true;
        }
        bool enabled = value == "on";
        Properties.Settings1.Default.ChatDragEnabled = enabled;
        Properties.Settings1.Default.Save();
        toggleBtn.AllowDragging = enabled;
        if (coreGuiWindow != null) coreGuiWindow.DragEnabled = enabled;
        RichChatBox.AppendSystemMessage(chatBox, enabled
            ? "Dragging enabled. Drag the history or input; on the default theme, hold and drag the chat button."
            : "Dragging disabled. Chat position locked.");
        return true;
    }

    private bool HandleThemeCommand(string args)
    {
        string[] parts = (args ?? "").Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string theme = parts.FirstOrDefault() ?? "";
        if (parts.Length > 1 && (theme != "2014" || parts.Length > 2 || parts[1] is not ("classic" or "docked")))
        {
            RichChatBox.AppendSystemMessage(chatBox, "Usage: /theme 2014 [classic|docked]"); return true;
        }
        if (!ValidThemes.Contains(theme))
        {
            RichChatBox.AppendSystemMessage(chatBox, "Usage: /theme <default|2014|2016|2018>");
            return true;
        }
        if (theme == "2014") Properties.Settings1.Default.Theme2014Mode = parts.Length == 2 ? parts[1] : "classic";
        ApplyTheme(theme, true);
        RichChatBox.AppendSystemMessage(chatBox, $"Theme changed to {theme}.");
        return true;
    }

    private void ApplySavedTheme()
    {
        try { dragOffsets = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Point>>(Properties.Settings1.Default.ChatDragOffsets) ?? new(); }
        catch (System.Text.Json.JsonException) { dragOffsets = new(); }
        try { frameSizes = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Size>>(Properties.Settings1.Default.ChatFrameSizes) ?? new(); }
        catch (System.Text.Json.JsonException) { frameSizes = new(); }
        string saved = Properties.Settings1.Default.ChatTheme;
        ApplyTheme(ValidThemes.Contains(saved) ? saved.ToLowerInvariant() : "default", false);
    }

    private void ApplyTheme(string theme, bool persist)
    {
        activeTheme = theme;
        RichChatBox.SetTheme(theme);
        bool historical = theme != "default";
        mainContainer.Visible = !historical && !isWindowHidden;
        toggleBtn.Visible = coreGui2016==null;
        toggleBtn.Location = historical ? Point.Empty : new Point(115, 2);
        useWholeWindowFade = !historical;
        toggleBtn.AllowDragging = Properties.Settings1.Default.ChatDragEnabled;
        if (historical)
        {
            // Existing controls are only a backing message/input model here.
            Opacity = 1;
            Size = new Size(45, 45);
            if (coreGuiWindow == null)
            {
                coreGuiWindow = new CoreGuiChatWindow { Owner = this };
                coreGuiWindow.InputClicked += StartChatMode;
                coreGuiWindow.SenderClicked += BeginWhisper;
                coreGuiWindow.PositionChanged += (_, _) => SaveHistoricalLayout();
                coreGuiWindow.Resized += SaveHistoricalLayout;
            }
            coreGuiWindow.DragEnabled = Properties.Settings1.Default.ChatDragEnabled;
            coreGuiWindow.MessageOffset = dragOffsets.GetValueOrDefault(theme);
            coreGuiWindow.InputOffset = dragOffsets.GetValueOrDefault("2014-input");
            coreGuiWindow.FrameSize = frameSizes.GetValueOrDefault(theme);
            RefreshCoreGui();
        }
        else
        {
            RestoreGamePlacement();
            coreGuiWindow?.Hide();
            mainContainer.Location = new Point(7, 54);
            var savedPanel=Properties.Settings1.Default.ChatContainerSize;
            if(savedPanel.Width<180||savedPanel.Height<100)savedPanel=new Size(472,297);
            mainContainer.Size=savedPanel;
            // A temporary CoreGui theme uses a 45x45 owner window. Older builds
            // accidentally persisted it as the default chat size.
            var savedWindow=Properties.Settings1.Default.WindowSize;
            Properties.Settings1.Default.WindowSize=new Size(
                Math.Max(savedWindow.Width,mainContainer.Right+21),
                Math.Max(savedWindow.Height,mainContainer.Bottom+49));
            Size = Properties.Settings1.Default.WindowSize;
            mainContainer.BackColor = Color.FromArgb(35, 45, 55);
            mainContainer.Padding = new Padding(10, 10, 30, 10);
            chatBox.BackColor = mainContainer.BackColor;
            chatBox.Font = RichChatBox.GetThemeFont();
            inputBox.BackColor = Color.FromArgb(25, 25, 25);
            inputBox.ForeColor = Color.White;
            inputBox.Font = RichChatBox.GetThemeFont(bold: true, input: true);
            inputBox.BorderStyle = BorderStyle.FixedSingle;
            inputBox.Height = 45;
            inputBox.ShowSendArrow = true;
            inputBox.TextInset = 10;
            inputBox.PlaceholderColor = Color.FromArgb(180, 200, 200, 200);
            SetRoundedRegion(mainContainer, 20);
            SetRoundedRegion(inputBox, 10);
            if (NativeMethods.GetWindowRect(robloxProcess.MainWindowHandle, out var rect))
                Location = new Point(rect.Left + currentOffset.X, rect.Top + currentOffset.Y);
        }
        ApplyTextStyle();
        if (persist)
        {
            Properties.Settings1.Default.ChatTheme = theme;
            Properties.Settings1.Default.Save();
        }
    }

    private void RefreshCoreGui()
    {
        if (activeTheme == "default" || coreGuiWindow == null || IsDisposed) return;
        if(settingsAutomationActive){coreGuiWindow.Hide();return;}
        var window = robloxProcess.MainWindowHandle;
        toggleBtn.Visible = coreGui2016==null && window != IntPtr.Zero && !NativeMethods.IsIconic(window);
        bool shown = !isWindowHidden && !NativeMethods.IsIconic(window) && window != IntPtr.Zero;
        if (!shown) { coreGuiWindow.Hide(); if (isWindowHidden) RestoreGamePlacement(); return; }
        var viewport = HistoricalViewport();
        if (viewport.Width <= 0 || viewport.Height <= 0) return;
        coreGuiWindow.TopMost = IsRobloxForegroundProcess();
        if(coreGui2016!=null)
        {
            coreGuiWindow.DragEnabled=false;
            coreGuiWindow.MessageOffset=Point.Empty;
            coreGuiWindow.InputOffset=Point.Empty;
            coreGuiWindow.FrameSize=Size.Empty;
        }
        coreGuiWindow.DarkText = DarkText;
        coreGuiWindow.Docked = Dock2014;
        if(chatBox.TextLength!=cachedCoreGuiTextLength) {
            cachedCoreGuiTextLength=chatBox.TextLength;
            cachedCoreGuiLines=RichChatBox.GetLines(chatBox);
        }
        string historicalInput=Services.LocalRobloxAccount.Mask(rawInputText);
        int historicalCaret=inputBox.CaretIndex;
        if(whisperTarget!=null){string prefix=$"[{whisperTarget}] ";historicalInput=prefix+historicalInput;historicalCaret+=prefix.Length;}
        coreGuiWindow.RefreshSurface(activeTheme, viewport, cachedCoreGuiLines,
            historicalInput, historicalCaret, isChatting, shown);
        var message = coreGuiWindow.MessageScreenBounds;
        Location = new Point(Math.Clamp(message.Right + 6, viewport.Left, Math.Max(viewport.Left, viewport.Right - Width)), message.Top);
    }
}
