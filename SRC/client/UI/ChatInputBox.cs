namespace RobloxChatLauncher.UI
{
    // --------------------------------------------------
    // Custom input box (fake caret, custom paint)
    // --------------------------------------------------
    using System.ComponentModel;
    using System.Drawing;
    using System.Windows.Forms;

    using RobloxChatLauncher.Localization;

    class ChatInputBox : TextBox
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsChatting { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string RawText { get; set; } = "";
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int CaretIndex { get; set; } = 0;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowSendArrow { get; set; } = true;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int TextInset { get; set; } = 10;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color PlaceholderColor { get; set; } = Color.FromArgb(180, 200, 200, 200);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string? WhisperTarget { get; set; }

        bool caretVisible = true;
        System.Windows.Forms.Timer caretTimer;

        public ChatInputBox()
        {
            SetStyle(ControlStyles.UserPaint, true);
            ReadOnly = true;
            BorderStyle = BorderStyle.FixedSingle;

            // We need to use a fake caret because since we never truly focus the overlay,
            // the win32 caret doesn't work
            caretTimer = new System.Windows.Forms.Timer { Interval = 500 };
            caretTimer.Tick += (s, e) =>
            {
                caretVisible = !caretVisible;
                this.Invalidate(false);
            };
            caretTimer.Start();
        }

        protected override void OnResize(EventArgs e)
        {
            this.Invalidate(); // Forces a clean redraw of the text and arrow
            base.OnResize(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) caretTimer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            string displayText;
            Color color;

            if (!IsChatting && string.IsNullOrEmpty(RawText))
            {
                displayText = $"{Strings.ChatInputBoxText}";
                color = PlaceholderColor;
            }
            else
            {
                // Logic: Insert the "|" at the CaretIndex position
                // Ensure index stays within bounds safely
                int safeIndex = Math.Max(0, Math.Min(CaretIndex, RawText.Length));

                if (IsChatting && caretVisible)
                {
                string prefix=WhisperTarget==null?"":$"[{WhisperTarget}] ";
                displayText = prefix+RawText.Insert(safeIndex, "|");
                }
                else
                {
                    // Draw a space where the caret would be to prevent text "jumping"
                    string prefix=WhisperTarget==null?"":$"[{WhisperTarget}] ";
                    displayText = prefix+RawText.Insert(safeIndex, " ");
                }
                color = ForeColor;
            }

            // Set a 10px margin so text doesn't hit the edge
            int arrowSpace = ShowSendArrow ? 40 : TextInset;
            Rectangle textRect = new Rectangle(TextInset, 0, Math.Max(1, ClientRectangle.Width - TextInset - arrowSpace), ClientRectangle.Height);

            var saved = e.Graphics.Save();
            e.Graphics.SetClip(textRect);
            int x = textRect.X;
            foreach (string run in EmojiPicker.Runs(displayText))
            {
                if (EmojiImages.Draw(e.Graphics, run, new Rectangle(x, (Height - 20) / 2, 20, 20))) { x += 20; continue; }
                int width = TextRenderer.MeasureText(run, Font, new Size(int.MaxValue, Height), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                TextRenderer.DrawText(e.Graphics, run, Font, new Rectangle(x, 0, width + 2, Height), color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                x += width;
            }
            e.Graphics.Restore(saved);

            // Draw the arrow icon on the right
            if (ShowSendArrow)
            {
                TextRenderer.DrawText(e.Graphics, "➤", Font,
                    new Rectangle(Width - 35, 0, 30, Height), color,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
