using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

using RobloxChatLauncher.Localization;
using RobloxChatLauncher.Services;
using RobloxChatLauncher.UI;
using RobloxChatLauncher.Utils;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace RobloxChatLauncher
{
    // --------------------------------------------------
    // Chat window
    // --------------------------------------------------
    public partial class ChatForm : Form
    {
        // This is required to hide the overlay from the alt-tab menu
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // 0x00000080 is WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x00000080;
                // WS_EX_COMPOSITED: tells Windows to composite (double-buffer) all child windows together
                // This is the biggest single fix to reduce flickering
                cp.ExStyle |= 0x02000000;
                return cp;
            }
        }

        Process robloxProcess;
        private volatile bool closeWhenShown;
        private bool formShown;
        Panel mainContainer; // New container for the window
        RichTextBox chatBox;
        ChatInputBox inputBox;
        RoundButton toggleBtn;
        ResizeGrip resizeGrip;
        Label resetBtn;
        internal bool isWindowHidden { get; private set; } = false;
        bool overlayTopMostActive;

        System.Windows.Forms.Timer fadeTimer;

        IntPtr winEventHook = IntPtr.Zero;
        NativeMethods.WinEventDelegate winEventDelegate;

        private Point defaultOffset = new Point(10, 40); // Original offset
        private Point currentOffset = new Point(10, 40); // Tracks user customization
        private bool isUserMovingWindow = false;

        // Save settings periodically in case of crashes
        private bool _settingsDirty = false;
        private System.Windows.Forms.Timer _autoSaveTimer;

        float chatOnOpacity = 1.0f;
        float chatOffOpacity = 0.7f;
        float targetOpacity = 0.7f;
        bool useWholeWindowFade = true;
        const float fadeStep = 0.05f;

        bool isChatting;
        string rawInputText = "";
        string? whisperTarget;

        // Sets a rounded region for the given control as
        // Windows Forms does not natively support rounded corners.
        private void SetRoundedRegion(Control control, int radius)
        {
            System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();
            path.StartFigure();
            path.AddArc(new Rectangle(0, 0, radius, radius), 180, 90);
            path.AddArc(new Rectangle(control.Width - radius, 0, radius, radius), 270, 90);
            path.AddArc(new Rectangle(control.Width - radius, control.Height - radius, radius, radius), 0, 90);
            path.AddArc(new Rectangle(0, control.Height - radius, radius, radius), 90, 90);
            path.CloseFigure();
            control.Region = new Region(path);
        }

        private int historicalRefreshQueued;
        void OnRobloxLocationChanged(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime)
        {
            if (hwnd != robloxProcess.MainWindowHandle || isUserMovingWindow) return;
            if (activeTheme != "default")
            {
                if (!IsDisposed && IsHandleCreated && Interlocked.Exchange(ref historicalRefreshQueued, 1) == 0)
                {

                    BeginInvoke((MethodInvoker)(() =>
                    {
                        Interlocked.Exchange(ref historicalRefreshQueued, 0);
                        if (!IsDisposed) RefreshCoreGui();
                    }));
                }
                return;
            }
            if (hwnd != robloxProcess.MainWindowHandle || isUserMovingWindow)
                return;

            if (NativeMethods.IsIconic(hwnd))
            {
                BeginInvoke((MethodInvoker)(() =>
                    WindowState = FormWindowState.Minimized));
                return;
            }

            NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect);

            BeginInvoke((MethodInvoker)(() =>
            {
                if (WindowState == FormWindowState.Minimized)
                    WindowState = FormWindowState.Normal;

                // Use the dynamic offset instead of +10, +40
                Location = new Point(rect.Left + currentOffset.X, rect.Top + currentOffset.Y);
            }));
        }

        public bool IsRobloxForegroundProcess()
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero)
                return false;

            NativeMethods.GetWindowThreadProcessId(fg, out uint pid);
            return pid == (uint)robloxProcess.Id;
        }

        internal void HandleGlobalMouseDown(Point screen)
        {
            if(!isChatting||ForwardingNativeInput||IsDisposed||Disposing)return;
            bool inside=activeTheme=="default"&&mainContainer.Visible&&
                RectangleToScreen(mainContainer.ClientRectangle).Contains(screen);
            inside|=coreGuiWindow is { Visible:true } historical&&
                (historical.MessageScreenBounds.Contains(screen)||historical.InputScreenBounds.Contains(screen));
            inside|=coreGui2016 is { Visible:true } modern&&modern.ContainsInteractiveScreenPoint(screen);
            inside|=classicGui is { Visible:true } classic&&classic.ContainsInteractiveScreenPoint(screen);
            if(inside||!IsHandleCreated)return;
            BeginInvoke((MethodInvoker)(() =>
            {
                if(isChatting&&!IsDisposed&&!Disposing)CancelChatMode();
            }));
        }

        public void ToggleVisibility()
        {
            this.Invoke((MethodInvoker)delegate
            {
                isWindowHidden = !isWindowHidden;
                mainContainer.Visible = activeTheme == "default" && !isWindowHidden;
                RefreshCoreGui();

                // Also hide the toggle button itself when the window is hidden
                // toggleBtn.Visible = !isWindowHidden;

                // Update the visual state of the button
                toggleBtn.IsActive = !isWindowHidden;
                toggleBtn.Invalidate();
            });
        }

        public ChatForm(Process proc, bool isForceRun)
        {
            robloxProcess = proc;
            robloxProcess.EnableRaisingEvents = true;
            robloxProcess.Exited += RobloxProcess_Exited;

            this.ShowInTaskbar = false; // Hide the GUI process from taskbar

            // Form Transparency/Styling
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.Magenta;
            this.TransparencyKey = Color.Magenta; // Makes form background invisible
            this.Width = 500;
            this.Height = 400;
            this.TopMost = true;
            this.DoubleBuffered = true; // Reduce flicker

            // Auto-save timer to periodically save settings if they have changed
            _autoSaveTimer = new System.Windows.Forms.Timer { Interval = 5000 }; // Save every 5 seconds if changed
                                                                                 // This ensures that if the application crashes or is killed,
                                                                                 // we won't lose more than 5 seconds of position/size changes 
            _autoSaveTimer.Tick += (s, e) =>
            {
                if (_settingsDirty)
                {
                    SaveSettingsToDisk();
                    _settingsDirty = false;
                }
            };
            _autoSaveTimer.Start();

            // Circular Toggle Button (Parented to Form, not Container)
            toggleBtn = new RoundButton
            {
                Location = new Point(115, 2),
                Size = new Size(45, 45),
                Cursor = Cursors.Hand
            };

            toggleBtn.Dragged += (s, delta) =>
            {
                if (!Properties.Settings1.Default.ChatDragEnabled) return;
                if (activeTheme != "default" && coreGuiWindow != null)
                {
                    coreGuiWindow.MessageOffset = new Point(coreGuiWindow.MessageOffset.X + delta.X, coreGuiWindow.MessageOffset.Y + delta.Y);
                    RefreshCoreGui(); return;
                }
                isUserMovingWindow = true;
                // Update the offset based on drag
                currentOffset.X += delta.X;
                currentOffset.Y += delta.Y;

                // Immediate visual update
                this.Location = new Point(this.Location.X + delta.X, this.Location.Y + delta.Y);
            };

            toggleBtn.DragEnded += (s, e) =>
            {
                if (!Properties.Settings1.Default.ChatDragEnabled) return;
                if (activeTheme != "default") { SaveHistoricalLayout(); return; }
                isUserMovingWindow = false;

                // 1. Get where Roblox is right now
                NativeMethods.GetWindowRect(robloxProcess.MainWindowHandle, out NativeMethods.RECT rect);

                // 2. Calculate our current offset relative to Roblox's top-left
                int relativeX = this.Location.X - rect.Left;
                int relativeY = this.Location.Y - rect.Top;

                // 3. Snap check: If relative position is close to default, snap to it
                int snapDistance = 20; // Adjust as needed
                if (Math.Abs(relativeX - defaultOffset.X) < snapDistance &&
                    Math.Abs(relativeY - defaultOffset.Y) < snapDistance)
                {
                    currentOffset = defaultOffset;
                }
                else
                {
                    // Otherwise, save this new position as the permanent offset
                    currentOffset = new Point(relativeX, relativeY);
                }
                // Persist position
                Properties.Settings1.Default.WindowOffset = currentOffset;
                _settingsDirty = true; // Mark for auto-save

                this.Location = new Point(rect.Left + currentOffset.X, rect.Top + currentOffset.Y);
            };

            toggleBtn.Clicked += (_, _) => ToggleVisibility();
            this.Controls.Add(toggleBtn);

            // Load persisted values
            currentOffset = Properties.Settings1.Default.WindowOffset;
            this.Size = Properties.Settings1.Default.WindowSize;

            // Ensure the mainContainer uses the saved size
            mainContainer = new SmoothPanel
            {
                Location = new Point(7, 54),
                Size = Properties.Settings1.Default.ChatContainerSize, // THIS IS THE REAL SIZE OF THE CHAT WINDOW
                BackColor = Color.FromArgb(35, 45, 55), // Semi-transparent Dark Blue-Gray
                // Each number is: Left, Top, Right, Bottom padding respectively
                Padding = new Padding(10, 10, 30, 10) // Give text breathing room
            };

            // Apply rounded corners after the control is created
            mainContainer.HandleCreated += (s, e) => SetRoundedRegion(mainContainer, 20);

            // 2. Update Chat History (The top part)
            chatBox = new RichTextBox
            {
                Multiline = true,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(35, 45, 55),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Regular),
                TabStop = false,
                Margin = new Padding(0),
                HideSelection = false,
                // Scroll bars are disabled because there is an unremovable white background
                // behind the scroll bars that Windows stupidly forces us to have.
                ScrollBars = RichTextBoxScrollBars.None,
            };

            // 3. Update Input Bar (The bottom part)
            inputBox = new ChatInputBox
            {
                Dock = DockStyle.Bottom,
                Height = 45,
                BackColor = Color.FromArgb(25, 25, 25), // Darker than history
                ForeColor = Color.White,
                Enabled = false,
                // Margin is ignored by Dock, but Padding in the parent will now squeeze this
            };

            // Ensure the input box also has slightly rounded corners
            inputBox.HandleCreated += (s, e) => SetRoundedRegion(inputBox, 10);

            // Resize Grip
            resizeGrip = new ResizeGrip();
            // Anchor it to the bottom right
            resizeGrip.Location = new Point(mainContainer.Width - resizeGrip.Width, mainContainer.Height - resizeGrip.Height);
            resizeGrip.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            resizeGrip.ResizeDragged += (s, delta) =>
            {
                int newWidth = this.Width + delta.Width;
                int newHeight = this.Height + delta.Height;

                if (newWidth > 200 && newHeight > 150)
                {
                    // 1. Stop the layout engine temporarily
                    this.SuspendLayout();
                    mainContainer.SuspendLayout();

                    this.Size = new Size(newWidth, newHeight);
                    mainContainer.Size = new Size(mainContainer.Width + delta.Width, mainContainer.Height + delta.Height);

                    // 2. Refresh the rounded corners
                    SetRoundedRegion(mainContainer, 20);
                    SetRoundedRegion(inputBox, 10);

                    // 3. Resume and force a clean redraw
                    mainContainer.ResumeLayout();
                    this.ResumeLayout(true);
                    this.Update(); // Force instant redraw
                }
                // Update settings (memory only)
                Properties.Settings1.Default.WindowSize = this.Size;
                Properties.Settings1.Default.ChatContainerSize = mainContainer.Size;
                _settingsDirty = true; // Mark for auto-save
            };

            mainContainer.Controls.Add(chatBox);
            mainContainer.Controls.Add(inputBox);
            mainContainer.Controls.Add(resizeGrip);
            resizeGrip.BringToFront(); // Ensure it is above the chatBox

            // Hide real Win32 caret defensively
            chatBox.GotFocus += (s, e) => NativeMethods.HideCaret(chatBox.Handle);
            chatBox.MouseDown += (s, e) =>
            {
                NativeMethods.HideCaret(chatBox.Handle);
                if(e.Button==MouseButtons.Left&&RichChatBox.TryGetSenderAt(chatBox,e.Location,out string sender))BeginWhisper(sender);
            };
            inputBox.GotFocus += (s, e) => ActiveControl = null;
            inputBox.MouseDown += (s, e) => ActiveControl = null;
            inputBox.Click += (_, _) => StartChatMode();

            fadeTimer = new System.Windows.Forms.Timer { Interval = 75 };
            fadeTimer.Tick += UpdateOpacity;
            fadeTimer.Start();

            winEventDelegate = OnRobloxLocationChanged;

            winEventHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero,
                winEventDelegate,
                (uint)robloxProcess.Id,
                0,
                NativeMethods.WINEVENT_OUTOFCONTEXT);

            // ----- Reset Button in the bottom right corner -----
            resetBtn = new Label
            {
                Text = "1:1",
                Size = new Size(30, 20),
                ForeColor = Color.DarkGray,
                BackColor = Color.Transparent, // Let it blend with the panel
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 7, FontStyle.Bold),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };

            // Position it in the alley created by the padding
            // Right edge, and just above the Resize Grip
            resetBtn.Location = new Point(mainContainer.Width - 25, mainContainer.Height - 45);

            resetBtn.Click += (s, e) =>
            {
                this.Size = new Size(500, 400);
                mainContainer.Size = new Size(472, 297);
                SetRoundedRegion(mainContainer, 20);
                SetRoundedRegion(inputBox, 10);
            };

            mainContainer.Controls.Add(resetBtn);
            resetBtn.BringToFront(); // Ensure it stays above the chatBox

            this.Controls.Add(mainContainer);

            // Ensure it is layered correctly
            mainContainer.BringToFront();
            ApplySavedTheme();

            // Manually trigger the first position sync
            NativeMethods.GetWindowRect(robloxProcess.MainWindowHandle, out NativeMethods.RECT initialRect);
            this.Location = new Point(initialRect.Left + currentOffset.X, initialRect.Top + currentOffset.Y);

            // --------------------------------------------
            // --- Roblox Log Monitor for JobID changes ---
            // --------------------------------------------
            // Initialize the Roblox Log Monitor
            _robloxService = new Services.RobloxAreaService();

            _robloxService.OnSystemMessage += (_, msg) =>
            {
                this.Invoke((MethodInvoker)(() =>
                {
                    RichChatBox.AppendSystemMessage(chatBox, msg);
                }));
            };

            // Subscribe to OnGameJoin event to detect when the player joins a new server
            _robloxService.OnGameJoin += async (s, e) =>
            {
                if(closeWhenShown || IsDisposed || Disposing || _robloxService==null)return;
                var newJobId = _robloxService.Data.JobId;
                if (this.channelId == newJobId)
                    return;

                this.channelId = newJobId;

                this.Invoke((MethodInvoker)delegate
                {
                    // chatBox.AppendText($"[{Strings.Server}]: {Strings.SwitchingServer}\r\n");
                });

                await RestartWebSocketAsync(); // make sure chat reconnects to the new server
            };

            // Start watching logs, passing the Roblox process for session tracking
            // Start after Show completes: reading an existing log can execute
            // synchronously and raise game-join events that require a window handle.
            Shown += async (_, _) =>
            {
                await Task.Yield();
                var monitor=_robloxService;
                if(closeWhenShown || IsDisposed || Disposing || monitor==null)return;
                try { await monitor.Start(robloxProcess,isForceRun); }
                catch(OperationCanceledException) when(closeWhenShown || IsDisposed || Disposing) { }
                catch(Exception ex)
                {
                    if(!closeWhenShown && !IsDisposed && !Disposing)
                        RichChatBox.AppendSystemMessage(chatBox,"Could not monitor Roblox: "+ex.Message);
                }
            };

            // Initial check: If we can't find a JobID yet, wait for the log monitor to catch it
            channelId = "global";

            // --- End Roblox Log Monitor ---


            RichChatBox.AppendText(chatBox, Strings.StartupText);

#if DEBUG
            RichChatBox.ShowcasePreview(chatBox);
#endif

#if !DEBUG && OFFICIAL_RELEASE
            // Update checker. We install in OnFormClosed or if the user types /update
            // This should run exactly once on form load
            DateTime lastCheck = Properties.Settings1.Default.LastUpdateCheckUTC;
            if ((DateTime.UtcNow - lastCheck).TotalHours >= 1) // Only check for updates if it's been more than an hour since the last check
            {
                _ = Task.Run(async () =>
                {
                    await UpdateService.CheckAndDownloadUpdate(UpdateMode.Background, true, (status) => // true for includePrerelease since we don't have any stable releases yet
                                                                                                        // this will be changed to false when we have stable releases
                    {
                        this.Invoke(new Action(() => RichChatBox.AppendText(chatBox, status)));
                    });

                    // Update the timestamp
                    Properties.Settings1.Default.LastUpdateCheckUTC = DateTime.UtcNow;
                    Properties.Settings1.Default.Save();
                });
            }
#endif
        }

        private void RobloxProcess_Exited(object sender, EventArgs e)
        {
            closeWhenShown=true;
            _robloxService?.Dispose();
            _robloxService = null;
            if(!formShown||!IsHandleCreated||IsDisposed||Disposing)return;
            try
            {
                BeginInvoke((MethodInvoker)(() =>
                {
                    if(!IsDisposed&&!Disposing)Close();
                }));
            }
            catch(InvalidOperationException) { /* Handle creation/teardown owns closing. */ }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            formShown=true;
            if(activeTheme=="default"&&!isWindowHidden)
            {
                // Reapply after the native window exists. This is the same path
                // used by /theme default and prevents a hidden startup panel.
                ApplyTheme("default",false);
                mainContainer.Visible=true;
                mainContainer.BringToFront();
                TopMost=false;
                TopMost=true;
                Invalidate(true);
                Update();
            }
            if(closeWhenShown||robloxProcess.HasExited)
                BeginInvoke((MethodInvoker)(() => { if(!IsDisposed&&!Disposing)Close(); }));
        }

        void UpdateOpacity(object sender, EventArgs e) // Z-order scoping is also here so we can use the same timer. Please do not make it its own timer
        {
            // The default panel has no separate surface. Keep it visible and
            // above Roblox after the watcher attaches or Roblox restores.
            if(activeTheme=="default"&&!isWindowHidden&&IsRobloxForegroundProcess())
            {
                if(!mainContainer.Visible)mainContainer.Visible=true;
                if(!Visible)Show();
                mainContainer.BringToFront();
            }
            RefreshClassicGui();
            RefreshCoreGui();
            if (emojiPicker?.Visible == true && !IsRobloxForegroundProcess()) emojiPicker.Hide();
            // Z-order scoping
            // The overlay will always stay above Roblox,
            // but not necessarily above other windows if those windows are above Roblox.
            bool robloxActive = this.IsRobloxForegroundProcess();

            if (robloxActive && !overlayTopMostActive)
            {
                TopMost = true;
                overlayTopMostActive = true;
            }
            else if (!robloxActive && overlayTopMostActive)
            {
                TopMost = false;
                overlayTopMostActive = false;
            }
            // END Z-order scoping

            // Fade logic
            if (!useWholeWindowFade)
            {
                if (Opacity != 1.0) Opacity = 1.0;
                return;
            }
            if (Math.Abs(Opacity - targetOpacity) < 0.01f)
                return;
            Opacity += Opacity < targetOpacity ? fadeStep : -fadeStep;
        }

        public void StartChatMode()
        {
            isChatting = true;
            // Don't clear the input bar
            // rawInputText = "";
            targetOpacity = chatOnOpacity;
            SyncInput();
        }

        public void AppendTextFromKey(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // Insert text at current caret position
            rawInputText = rawInputText.Insert(inputBox.CaretIndex, text);

            // Move caret forward by the length of inserted text
            inputBox.CaretIndex += text.Length;
            SyncInput();
        }

        public void Backspace()
        {
            if(whisperTarget!=null&&rawInputText.Length==0)
            {
                whisperTarget=null;rawInputText="";inputBox.CaretIndex=0;SyncInput();return;
            }
            if (rawInputText.Length > 0 && inputBox.CaretIndex > 0)
            {
                // Remove 1 char to the left of caret
                int previous = System.Globalization.StringInfo.ParseCombiningCharacters(rawInputText).LastOrDefault(i => i < inputBox.CaretIndex);
                rawInputText = rawInputText.Remove(previous, inputBox.CaretIndex - previous);
                inputBox.CaretIndex = previous;
                SyncInput();
            }
        }

        private void BeginWhisper(string sender)
        {
            if(string.IsNullOrWhiteSpace(sender))return;
            whisperTarget=sender;
            rawInputText="";
            inputBox.CaretIndex=0;
            StartChatMode();
        }

        public void CancelChatMode()
        {
            isChatting = false;
            targetOpacity = chatOffOpacity;
            SyncInput();
        }

        void SyncInput()
        {
            inputBox.RawText = Services.LocalRobloxAccount.Mask(rawInputText);
            inputBox.WhisperTarget=whisperTarget;
            inputBox.IsChatting = isChatting;

            // Safety check to prevent index out of bounds
            if (inputBox.CaretIndex > rawInputText.Length)
                inputBox.CaretIndex = rawInputText.Length;

            inputBox.Invalidate();
            UpdateEmojiSuggestions();
        }

        public string GetInputText() => rawInputText;

        // Handle Arrow Keys
        public void HandleNavigation(Keys key)
        {
            if (key == Keys.Left)
            {
                inputBox.CaretIndex = System.Globalization.StringInfo.ParseCombiningCharacters(rawInputText).LastOrDefault(i => i < inputBox.CaretIndex);
            }
            else if (key == Keys.Right)
            {
                inputBox.CaretIndex = System.Globalization.StringInfo.ParseCombiningCharacters(rawInputText).FirstOrDefault(i => i > inputBox.CaretIndex, rawInputText.Length);
            }
            else if (key == Keys.Home)
            {
                inputBox.CaretIndex = 0;
            }
            else if (key == Keys.End)
            {
                inputBox.CaretIndex = rawInputText.Length;
            }

            // Reset timer so caret is immediately visible when moving
            // caretVisible = true; 
            inputBox.Invalidate();
        }

        // Helper to save everything
        // Windows manages settings for us so we don't have to
        // manage our own appdata folder or worry about file permissions
        private void SaveSettingsToDisk()
        {
            Properties.Settings1.Default.WindowOffset = currentOffset;
            if (activeTheme == "default" && WindowState == FormWindowState.Normal)
            {
                Properties.Settings1.Default.WindowSize = this.Size;
                Properties.Settings1.Default.ChatContainerSize = mainContainer.Size;
            }
            Properties.Settings1.Default.Save();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Final save of all settings
            _autoSaveTimer?.Stop(); // First stop the auto-save timer to prevent it from trying to save while we're disposing
            SaveSettingsToDisk(); // Final forced save before exit

            base.OnFormClosing(e);
        }
    }
}
