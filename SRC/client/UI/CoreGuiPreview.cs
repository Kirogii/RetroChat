using System.Drawing;
using System.Drawing.Imaging;
using RobloxChatLauncher.Utils;

namespace RobloxChatLauncher.UI;

internal static class CoreGuiPreview
{
    // Offline renderer check: no registry changes, Roblox launch, or chat connection.
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        using(var health=new Bitmap(145,7))
        {
            if(ChatForm.ReadNativeHealthFraction(health)!=1)throw new InvalidOperationException("Missing health bar must mean full health.");
            using(var g=Graphics.FromImage(health))g.FillRectangle(Brushes.Lime,15,0,55,7);
            if(Math.Abs(ChatForm.ReadNativeHealthFraction(health)-.5f)>.01f)throw new InvalidOperationException("Visible half-health bar must remain readable.");
            using(var g=Graphics.FromImage(health))g.Clear(Color.Gray);
            if(ChatForm.ReadNativeHealthFraction(health)!=1)throw new InvalidOperationException("Disappearing health bar must restore full health.");
        }
        if(TextureRoster.LocallyAdvanced(0,1000,6000)!=5)throw new InvalidOperationException("Roster clock must advance between server updates");
        using(var roster=new TextureRoster { Size=new Size(248,134),Members=new[]{new ChatMember("1","VisiblePlayerName",null,60,120,-240,"EDT")} }) {
            using var pixels=roster.CreateBitmap();
            if(pixels.GetPixel(80,60).A is 0 or 255) throw new InvalidOperationException("Roster texture alpha was lost");
            pixels.Save(Path.Combine(output,"coregui-2013-roster-transparent.png"));
        }
        foreach (string id in new[] { "45880668", "45880710", "54071825", "94692054", "94691980", "RecordStop" })
            if (CoreGuiTextures.Get(id)==null) throw new InvalidOperationException("Missing bundled texture: "+id);
        foreach(string id in new[]{"Hamburger","Chat","Backpack","EmotesIcon","EmotesIconDown","SettingsLeft","SettingsRight","SliderBarLeft","SliderBarRight","SliderSelectedBarLeft","SliderSelectedBarRight","MenuBackground","MenuButton","MenuSelection","PlayersTabIcon","GameSettingsTab","ReportAbuseTab","HelpTab","RecordTab"})
            if(CoreGui2016Textures.Get(id)==null)throw new InvalidOperationException("Missing bundled 2016 texture: "+id);
        using(var settingsPreview=new CoreGui2016 { Size=new Size(1280,720) })
        {
            var forwarded=new List<string>();settingsPreview.ActionRequested+=forwarded.Add;
            settingsPreview.ToggleMenu();settingsPreview.ReopenSettings();
            foreach(var pair in new Dictionary<string,string>{{"Shift Lock Switch","Off"},{"Camera Mode","Default (Classic)"},{"Movement Mode","Default (Keyboard)"},{"Camera Sensitivity","5"},{"Volume","8"},{"Fullscreen","Off"},{"Graphics Mode","Manual"},{"Graphics Quality","6"}})
                settingsPreview.SetSettingValue(pair.Key,pair.Key+": "+pair.Value);
            foreach(var pair in new Dictionary<string,string>
            {
                ["Maximum Frame Rate"]="Default (60 FPS)",["Automatic Translations"]="Off",
                ["Game Language"]="Unavailable",["Automatic Chat Translation"]="On",["Chat Translation Language"]="English",
                ["Option to View Untranslated Message"]="Off",["In-game friends chat notifications"]="On",
                ["Output Device"]="Default Output",["Haptics"]="On",["Background transparency"]="0",["Text size"]="0",
                ["UI navigation toggle"]="On",["Performance Stats"]="Off",["MicroProfiler"]="Off",
                ["Camera Inverted"]="Off",["Developer Console"]="Open",["People's Names"]="Show",["My Badges"]="Show"
            })settingsPreview.SetSettingValue(pair.Key,pair.Key+": "+pair.Value);
            using(var pixels=settingsPreview.CreateBitmap())pixels.Save(Path.Combine(output,"coregui-2016-settings.png"));
            forwarded.Clear();
            settingsPreview.ActivateAction("setting-step:Volume:right");
            if(!settingsPreview.MenuOpen||forwarded.Count!=0)
                throw new InvalidOperationException("Settings control did not forward its one-step action");
            settingsPreview.ReopenSettings();
            if(!settingsPreview.MenuOpen)throw new InvalidOperationException("Settings page did not return after native adjustment");
            settingsPreview.ActivateAction("setting-set:Volume:3");
            if(!settingsPreview.MenuOpen||forwarded.Count!=0)
                throw new InvalidOperationException("Slider target did not forward its requested value");
            settingsPreview.ReopenSettings("Volume");
            settingsPreview.HandleMenuKey(System.Windows.Forms.Keys.Left);
            if(forwarded.Count!=0)
                throw new InvalidOperationException("Keyboard adjustment lost its selected row");
            settingsPreview.SetBusy(true);
            settingsPreview.FlushPendingSlider();
            if(forwarded.Count!=0)throw new InvalidOperationException("Slider must wait for the current macro.");
            settingsPreview.SetBusy(false);
            settingsPreview.FlushPendingSlider();
            if(forwarded.Count!=1||forwarded[0]!="setting-set:Volume:2")
                throw new InvalidOperationException("Repeated slider edits must apply only the final value.");
            settingsPreview.FlushPendingSlider();
            if(forwarded.Count!=1)throw new InvalidOperationException("Slider changes must not be duplicated.");
            settingsPreview.ReopenSettings("My Badges");
            settingsPreview.HandleMenuKey(System.Windows.Forms.Keys.Down);
            using(var pixels=settingsPreview.CreateBitmap())pixels.Save(Path.Combine(output,"coregui-2016-settings-bottom.png"));
            settingsPreview.ActivateAction("setting-step:My Badges:left");
            if(forwarded.Last()!="setting-step:My Badges:left")throw new InvalidOperationException("Bottom settings must be editable.");
            if(RobloxChatLauncher.Services.RobloxSettingsAutomation.Settings.Length!=26)
                throw new InvalidOperationException("All 26 reference settings must be present.");
        }
        using(var modern=new CoreGui2016 { Size=new Size(1280,720) })
        {
            modern.SetChatVisible(false);
            modern.SetMembers(new[]{new ChatMember("1","VisiblePlayerName","1",65,120,-240,"EDT")});
            using(var roster2016=modern.CreateBitmap())roster2016.Save(Path.Combine(output,"coregui-2016-roster.png"));
            modern.ToggleMenu();
            using(var menu2016=modern.CreateBitmap())
            {
                if(menu2016.GetPixel(640,100).A==0)throw new InvalidOperationException("2016 menu shield was not rendered");
                menu2016.Save(Path.Combine(output,"coregui-2016-menu.png"));
            }
            var actions=new List<string>();modern.ActionRequested+=actions.Add;
            modern.ActivateAction("emotes");
            if(actions.Count!=1||actions[0]!="emotes")throw new InvalidOperationException("Emote button did not forward its action");
            actions.Clear();
            modern.ActivateAction("reset");
            using(var confirmation=modern.CreateBitmap())confirmation.Save(Path.Combine(output,"coregui-2016-reset.png"));
            modern.ActivateAction("back");
            if(actions.Count!=0)throw new InvalidOperationException("Cancel must not forward a Roblox action");
            modern.ActivateAction("reset");modern.ActivateAction("reset-confirm");
            if(modern.MenuOpen||actions.Single()!="reset-confirm")throw new InvalidOperationException("2016 Reset must close the overlay and use the shared 2013 action");
            modern.ToggleMenu();modern.ActivateAction("leave");modern.ActivateAction("leave-confirm");
            if(modern.MenuOpen||actions.Count!=2||actions[1]!="leave-confirm")throw new InvalidOperationException("2016 Leave must use the shared 2013 action");
            modern.ToggleMenu();modern.HandleMenuKey(System.Windows.Forms.Keys.R);modern.HandleMenuKey(System.Windows.Forms.Keys.Right);modern.HandleMenuKey(System.Windows.Forms.Keys.Enter);
            if(!modern.MenuOpen||actions.Count!=2)throw new InvalidOperationException("Keyboard cancel must stay in the menu without resetting");
            modern.HandleMenuKey(System.Windows.Forms.Keys.L);modern.HandleMenuKey(System.Windows.Forms.Keys.Right);modern.HandleMenuKey(System.Windows.Forms.Keys.Left);modern.HandleMenuKey(System.Windows.Forms.Keys.Enter);
            if(modern.MenuOpen||actions.Last()!="leave-confirm")throw new InvalidOperationException("L, arrows and Enter must confirm leave");
            modern.ToggleMenu();modern.HandleMenuKey(System.Windows.Forms.Keys.R);modern.HandleMenuKey(System.Windows.Forms.Keys.Enter);
            if(modern.MenuOpen||actions.Last()!="reset-confirm")throw new InvalidOperationException("R and Enter must confirm reset");
            actions.RemoveRange(2,actions.Count-2);
            modern.ToggleMenu();modern.ActivateAction("tab:Record");modern.ActivateAction("screenshot");
            if(modern.MenuOpen||actions.Last()!="screenshot")throw new InvalidOperationException("2016 screenshot must use the same shared action as 2013");
            modern.ToggleMenu();modern.ActivateAction("tab:Record");modern.ActivateAction("record");
            if(modern.MenuOpen||actions.Last()!="record")throw new InvalidOperationException("2016 record must use the same shared native F12 action as 2013");
            int actionCount=actions.Count;
            modern.SetMembers(Enumerable.Range(1,50).Select(i=>new ChatMember(i.ToString(),$"Player {i:00}",i.ToString())));
            modern.ScrollList(false,10000);
            if(modern.RosterScroll<=0)throw new InvalidOperationException("Roster must scroll beyond visible members");
            modern.ToggleMenu();modern.ScrollList(true,10000);
            if(modern.PlayerScroll<=0)throw new InvalidOperationException("Players page must scroll beyond visible members");
            using(var scrolled=modern.CreateBitmap())scrolled.Save(Path.Combine(output,"coregui-2016-players-scrolled.png"));
            modern.SetBusy(true);modern.ActivateAction("friend:1");
            if(actions.Count!=actionCount)throw new InvalidOperationException("Busy friend action must not dispatch twice");
            modern.SetBusy(false);modern.ActivateAction("resume");
            actions.Clear();modern.SetChatVisible(true);modern.ToggleMenu();modern.ActivateAction("resume");
            if(actions.Count!=2||actions.Any(a=>a!="toggle-chat"))throw new InvalidOperationException("Menu must hide then restore visible chat");
            modern.SetMembers(Array.Empty<ChatMember>());modern.ScrollList(false,0);modern.ToggleMenu();modern.ScrollList(true,0);
            if(modern.PlayerScroll!=0||modern.RosterScroll!=0)throw new InvalidOperationException("Shrinking lists must clamp scroll positions");
        }
        if (Services.LocalRobloxAccount.Mask("/cookie testsecret") != "/cookie **********") throw new InvalidOperationException("Cookie masking failed");
        using (var classic = new CoreGui2013 { Size = new Size(1280,720) })
        {
            classic.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            classic.Location = new Point(-20000,-20000);
            classic.Show();
            classic.SetMembers(Enumerable.Range(1,18).Select(i=>new ChatMember(i.ToString(),i==1?"Test Player":"Guest "+i,i==1?"1":null,291*i,1524*i)));
            classic.TogglePlayerListMaximized();
            using(var rosterPreview=new Bitmap(1280,720)) { using var rosterGraphics=Graphics.FromImage(rosterPreview);classic.DrawRosterPreview(rosterGraphics);rosterPreview.Save(Path.Combine(output,"coregui-2013-roster-maximized.png")); }
            classic.ToggleMenu();if(!classic.MenuOpen)throw new InvalidOperationException("Classic menu did not open");
            classic.ToggleMenu();
            if (classic.MenuOpen) throw new InvalidOperationException("Classic menu did not close");
        }
        using(var menu=new EscMenu2013Window { Size=new Size(1280,720),Location=new Point(-20000,-20000) })
        {
            using(var preview=menu.CreateBitmap()) {
                if(preview.GetPixel(394,130).A is 0 or 255)throw new InvalidOperationException("Menu panel must be translucent");
                if(preview.GetPixel(640,212).A!=255)throw new InvalidOperationException("Menu buttons must be opaque");
                preview.Save(Path.Combine(output,"coregui-2013.png"));
            }
            menu.ClickForTest("reset");if(menu.CurrentPage!="Reset")throw new InvalidOperationException("Reset button did not open its popup");
            using(var reset=menu.CreateBitmap())reset.Save(Path.Combine(output,"coregui-2013-reset.png"));
            menu.ClickForTest("cancel");menu.ClickForTest("leave");if(menu.CurrentPage!="Leave")throw new InvalidOperationException("Leave button did not open its popup");
        }
        foreach (string theme in new[] { "2016", "2018" })
        {
            RichChatBox.SetTheme(theme);
            using var font = RichChatBox.GetThemeFont(bold: true);
            if (font.Unit != GraphicsUnit.Pixel || font.Size != 18 || !font.Bold || !font.FontFamily.Name.Contains("Source Sans"))
                throw new InvalidOperationException($"Incorrect archived font: {font}");
        }
        int smooth = CoreGuiChatWindow.SmoothScroll(0, 54, 16);
        if (smooth <= 0 || smooth >= 54) throw new InvalidOperationException("Scroll must interpolate");
        for (int i = 0; i < 100; i++) smooth = CoreGuiChatWindow.SmoothScroll(smooth, 54, 16);
        if (smooth != 54) throw new InvalidOperationException("Scroll must settle");
        RichChatBox.SetTheme("default");
        using (var rich = new System.Windows.Forms.RichTextBox { Size = new Size(400, 100), BackColor = Color.FromArgb(35, 45, 55), ForeColor = Color.White })
        {
            _ = rich.Handle;
            rich.Select(0, 0);
            EmojiImages.Append(rich, "Image emoji: 😂 💀");
            if (!rich.Rtf.Contains("\\pict")) throw new InvalidOperationException("Default history lost emoji images");
            using var snapshot = new Bitmap(400, 100);
            rich.DrawToBitmap(snapshot, new Rectangle(0, 0, 400, 100));
            int colour = 0;
            for (int x = 0; x < snapshot.Width; x++) for (int y = 0; y < snapshot.Height; y++)
            { var c = snapshot.GetPixel(x, y); if (c.R > 180 && c.G > 100 && c.B < 100) colour++; }
            if (colour < 10) throw new InvalidOperationException("Default history did not render colour emoji");
            snapshot.Save(Path.Combine(output, "default-emoji.png"), ImageFormat.Png);
        }
        if (!EmojiPicker.Search("sku").Any(e => e.Emoji == "💀") || EmojiPicker.Expand(":skull:") != "💀")
            throw new InvalidOperationException("Emoji search/expansion failed");
        using (var controls = new CoreGuiChatWindow())
        {
            controls.ConfigureInteraction("2018", new Size(1280, 720), false);
            var layout = CoreGuiChatWindow.GetLayout("2018", new Size(1280, 720));
            controls.BeginPointer(Point.Empty, new Point(10, layout.Messages.Top + 10));
            controls.MovePointer(new Point(100, 80)); controls.EndPointer();
            if (controls.MessageOffset != Point.Empty) throw new InvalidOperationException("Drag off moved chat");
            controls.DragEnabled = true;
            controls.BeginPointer(Point.Empty, new Point(10, layout.Messages.Top + 10));
            controls.MovePointer(new Point(100, 80)); controls.EndPointer();
            if (controls.MessageOffset != new Point(100, 80)) throw new InvalidOperationException("Drag on failed");
            controls.MessageOffset = Point.Empty;
            controls.ConfigureInteraction("2018", new Size(1280, 720), false);
            controls.DragEnabled = false;
            controls.BeginPointer(Point.Empty, new Point(layout.Messages.Right - 3, layout.Messages.Bottom - 3));
            controls.MovePointer(new Point(50, 40)); controls.EndPointer();
            if (controls.FrameSize != new Size(layout.Messages.Width + 50, layout.Messages.Height + 40)) throw new InvalidOperationException("Resize failed");
        }
        using (var button = new RoundButton { AllowDragging = true })
        {
            int clicks = 0; button.Clicked += (_, _) => clicks++;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var mouse = new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, 5, 5, 0);
            typeof(RoundButton).GetMethod("OnMouseDown", flags)!.Invoke(button, new object[] { mouse });
            typeof(RoundButton).GetMethod("OnClick", flags)!.Invoke(button, new object[] { EventArgs.Empty });
            typeof(RoundButton).GetMethod("OnMouseUp", flags)!.Invoke(button, new object[] { mouse });
            if (clicks != 1) throw new InvalidOperationException("Quick click must toggle exactly once");
        }
        var lines = new[]
        {
            new RichChatBox.ChatLine("", "Chat '/?' or '/help' for a list of chat commands.", Color.White, DateTime.UtcNow),
            new RichChatBox.ChatLine("Builderman: ", "Welcome to the game!", Color.FromArgb(253, 41, 67), DateTime.UtcNow),
            new RichChatBox.ChatLine("Player3: ", "Emoji test 💀 😂 👨‍👩‍👧‍👦", Color.FromArgb(2, 184, 87), DateTime.UtcNow),
            new RichChatBox.ChatLine("Player2: ", "This longer message wraps inside the archived chat window, with the same square frames and text offsets.", Color.FromArgb(1, 162, 255), DateTime.UtcNow),
        };
        foreach (string theme in new[] { "2014", "2016", "2018" })
        {
            RichChatBox.SetTheme(theme);
            foreach (var size in new[] { new Size(1280, 720), new Size(1920, 1080) })
            {
                var (message, input) = CoreGuiChatWindow.GetLayout(theme, size);
                if (theme == "2014" && (message.Size != new Size(500, 120) || input != new Rectangle(0, size.Height - 20, size.Width, 20)))
                    throw new InvalidOperationException("2014 layout mismatch");
                if (theme != "2014" && (input.Height != (theme == "2016" ? 32 : 42) || input.Width != (int)Math.Round(size.Width * .3) || message.Bottom + 2 != input.Top))
                    throw new InvalidOperationException("Lua chat frame layout mismatch");
                int scroll = 0;
                using var image = CoreGuiChatWindow.DrawSurface(theme, size, lines, "", 0, false, false, ref scroll);
                if (theme == "2014" && image.GetPixel(message.Right - 20, message.Top + 1).A != 102)
                    throw new InvalidOperationException("2014 translucent history missing");
                using var editing = CoreGuiChatWindow.DrawSurface(theme, size, lines, "hello", 5, true, true, ref scroll);
                if(theme=="2016")
                {
                    var crop=Rectangle.Union(message,input);
                    int croppedScroll=0;
                    using var cropped=CoreGuiChatWindow.DrawSurface(theme,size,lines,"",0,false,false,ref croppedScroll,surface:crop);
                    if(cropped.Size!=crop.Size||cropped.GetPixel(10,message.Top-crop.Top+10).ToArgb()!=image.GetPixel(10,message.Top+10).ToArgb())
                        throw new InvalidOperationException("Cropped chat surface differs from the full renderer");
                }
                Point sample = theme == "2014" ? new Point(input.Right - 2, input.Top + 1) : new Point(input.X + 8, input.Y + 8);
                int alpha = theme == "2014" ? 191 : theme == "2016" ? 192 : 163;
                if (Math.Abs(image.GetPixel(sample.X, sample.Y).A - alpha) > 1 || Math.Abs(editing.GetPixel(sample.X, sample.Y).A - alpha) > 1)
                    throw new InvalidOperationException("Input opacity changed");
                if (image.GetPixel(size.Width - 1, size.Height / 2).A != 0)
                    throw new InvalidOperationException("Viewport should be transparent");
                image.Save(Path.Combine(output, $"{theme}-{size.Width}.png"), ImageFormat.Png);
                using var surface = new CoreGuiChatWindow { Bounds = new Rectangle(Point.Empty, size) };
                surface.Present(image); // Exercise UpdateLayeredWindow without displaying a window.
                if (theme == "2014")
                {
                    var dock = CoreGuiChatWindow.GetLayout(theme, size, docked: true);
                    if (dock.Input.Height != 32 || dock.Input.Bottom != size.Height) throw new InvalidOperationException("Docked bar bounds mismatch");
                }
            }
        }
    }
}
