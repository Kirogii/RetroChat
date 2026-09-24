using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Windows.Forms;
using RobloxChatLauncher.Localization;

namespace RobloxChatLauncher.Utils
{
    /// <summary>
    /// Provides static methods for appending formatted chat and system messages to a RichTextBox control.
    /// Handles coloring of sender names and system messages to enhance readability and maintain visual consistency
    /// with Roblox's default chat colors and automatically handles `\r\n` line breaks.
    /// </summary>
    public static class RichChatBox
    {
        private const float DefaultSize = 10f;
        private static readonly PrivateFontCollection _fontCollection = new PrivateFontCollection();
        private static readonly Dictionary<string, FontFamily> _montserratWeights =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly PrivateFontCollection _sourceSans = new PrivateFontCollection();
        private static string _theme = "default";
        public record ChatLine(string Prefix, string Text, Color Color, DateTime Created,string? ClickSender=null);
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RichTextBox, List<ChatLine>> Logs = new();
        private sealed record SenderRange(int Start,int Length,string Sender);
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RichTextBox, List<SenderRange>> SenderRanges = new();
        public static IReadOnlyList<ChatLine> GetLines(RichTextBox box) => Logs.GetOrCreateValue(box);
        public static void ClearLines(RichTextBox box) { Logs.GetOrCreateValue(box).Clear();SenderRanges.GetOrCreateValue(box).Clear(); }
        private static void RememberSender(RichTextBox box,int start,int length,string sender)
        {
            var ranges=SenderRanges.GetOrCreateValue(box);
            ranges.RemoveAll(r=>r.Start>=box.TextLength||r.Start+r.Length>box.TextLength);
            ranges.Add(new SenderRange(start,length,sender));
            if(ranges.Count>100)ranges.RemoveRange(0,ranges.Count-100);
        }
        public static bool TryGetSenderAt(RichTextBox box,Point point,out string sender)
        {
            int index=box.GetCharIndexFromPosition(point);
            var match=SenderRanges.GetOrCreateValue(box).LastOrDefault(r=>index>=r.Start&&index<r.Start+r.Length);
            sender=match?.Sender??string.Empty;
            return match!=null;
        }
        private static void Remember(RichTextBox box, string prefix, string text, Color color,string? clickSender=null)
        {
            var log = Logs.GetOrCreateValue(box);
            TrimOldHistory(box);
            log.Add(new ChatLine(prefix, text, color, DateTime.UtcNow,clickSender));
            if (log.Count > 50) log.RemoveAt(0);
        }

        private static void TrimOldHistory(RichTextBox box)
        {
            const int limit=60000,retain=40000;
            if(box.TextLength<=limit)return;
            string content=box.Text;
            int cut=content.IndexOf((char)10,Math.Max(0,content.Length-retain));
            if(cut<0)return;
            cut++;
            box.Select(0,cut);
            box.SelectedText=string.Empty;
            var ranges=SenderRanges.GetOrCreateValue(box);
            for(int i=ranges.Count-1;i>=0;i--)
            {
                var range=ranges[i];
                if(range.Start<cut)ranges.RemoveAt(i);
                else ranges[i]=range with { Start=range.Start-cut };
            }
        }
        static RichChatBox()
        {
            LoadAllFonts();
            LoadSourceSansFonts();
        }

        public static void SetTheme(string theme) => _theme = theme;

        public static Font GetThemeFont(bool bold = false, bool input = false)
        {
            if (_theme == "default")
                return GetMontserrat(bold ? "Bold" : "Medium", DefaultSize);

            // CoreGui uses pixel sizes. Keep measurement and outline rendering in
            // the same units even when Windows display scaling is above 100%.
            // The 2014 input itself used Size12; ChatInputBox requests bold and is
            // explicitly resized to the archived 20-pixel bar height.
            float size = _theme == "2014" && input ? 12f : 18f;
            if (_theme == "2014") bold = false;
            if (_sourceSans.Families.Length > 0)
                return new Font(_sourceSans.Families[0], size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            return new Font("Arial", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        }

        public static Font GetCoreGuiFont(float pixels,bool bold=false)
        {
            if(_sourceSans.Families.Length>0)
                return new Font(_sourceSans.Families[0],pixels,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);
            return new Font("Arial",pixels,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);
        }

        private static void LoadSourceSansFonts()
        {
            // Prefer the exact bundled ROHTML fonts, independently of the installed Roblox version.
            var assembly = Assembly.GetExecutingAssembly();
            bool bundledPro = assembly.GetManifestResourceInfo("SourceSansPro-Regular.ttf") != null;
            string? regularFont = bundledPro ? null : RobloxLocator.FindContentFile(Path.Combine("fonts", "SourceSansPro-Regular.ttf"));
            if (regularFont != null)
            {
                string folder = Path.GetDirectoryName(regularFont)!;
                foreach (string name in new[] { "SourceSansPro-Regular.ttf", "SourceSansPro-Bold.ttf" })
                {
                    string path = Path.Combine(folder, name);
                    if (File.Exists(path))
                    {
                        _sourceSans.AddFontFile(path);
                    }
                }
                if (_sourceSans.Families.Length > 0) return;
            }
            foreach (string resource in bundledPro ? new[] { "SourceSansPro-Regular.ttf", "SourceSansPro-Bold.ttf" } : new[] { "SourceSans3-Regular.ttf", "SourceSans3-Bold.ttf" })
            {
                using Stream? stream = assembly.GetManifestResourceStream(resource);
                if (stream == null) continue;
                byte[] dataBytes = new byte[stream.Length];
                stream.ReadExactly(dataBytes);
                // GDI+ AddMemoryFont can alias families when Montserrat is also loaded.
                // File-backed private registration keeps Source Sans' real family/weights.
                string cache=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RobloxChatLauncher","Fonts");
                Directory.CreateDirectory(cache);
                string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(dataBytes));
                string fontPath=Path.Combine(cache,hash+".ttf");
                if(!File.Exists(fontPath))File.WriteAllBytes(fontPath,dataBytes);
                _sourceSans.AddFontFile(fontPath);
                AddFontResourceEx(fontPath,0x10,IntPtr.Zero);
            }
        }
        [DllImport("gdi32.dll",CharSet=CharSet.Unicode)]
        private static extern int AddFontResourceEx(string file,uint flags,IntPtr reserved);

        /// <remarks>
        /// This method uses some ugly hacks because GDI+ is a nightmare to work with and usually maps the wrong fonts.
        /// It's completely random whether it will map the correct font or merge it into a generic "Montserrat" family, and this will change every time you run the app.
        /// Don't try to fix or simplify this unless you ensure the random chance to map the wrong font issue is actually resolved.
        /// Manually allocating memory blocks with VirtualAlloc and padding doesn't resolve the issue, and you may find that a fix will still cause it
        /// to be random if Medium or Black actually gets mapped to the correct weight, and sometimes Regular will map to Light because GDI+ is a joke.
        /// </remarks>
        private static void LoadAllFonts()
        {
            string[] fontFiles = {
                "Montserrat-Thin.ttf", "Montserrat-ThinItalic.ttf",
                "Montserrat-ExtraLight.ttf", "Montserrat-ExtraLightItalic.ttf",
                "Montserrat-Light.ttf", "Montserrat-LightItalic.ttf",
                "Montserrat-Regular.ttf", "Montserrat-Italic.ttf",
                "Montserrat-Medium.ttf", "Montserrat-MediumItalic.ttf",
                "Montserrat-SemiBold.ttf", "Montserrat-SemiBoldItalic.ttf",
                "Montserrat-Bold.ttf", "Montserrat-BoldItalic.ttf",
                "Montserrat-ExtraBold.ttf", "Montserrat-ExtraBoldItalic.ttf",
                "Montserrat-Black.ttf", "Montserrat-BlackItalic.ttf"
            };

            var assembly = Assembly.GetExecutingAssembly();

            foreach (var fileName in fontFiles)
            {
                string weight = Path.GetFileNameWithoutExtension(fileName).Replace("Montserrat-", "");

                using Stream? stream = assembly.GetManifestResourceStream(fileName);
                if (stream == null)
                    continue;

                byte[] fontData = new byte[stream.Length];
                stream.ReadExactly(fontData);

                // Define what this SPECIFIC file MUST be named by GDI+ to be considered "correct"
                // Most Montserrat weights include the weight in the family name (e.g. "Montserrat Medium")
                // except for Regular, Bold, Italic, and BoldItalic which GDI+ usually just calls "Montserrat"
                string expectedBase = weight.Replace("Italic", "").Trim();
                string expectedName = (expectedBase == "Regular" || expectedBase == "Bold" || expectedBase == "")
                    ? "Montserrat"
                    : $"Montserrat {expectedBase}";

                bool success = false;
                for (int attempt = 0; attempt < 20 && !success; attempt++) // Usually succeeds within 1-2 attempts for any given font, but we loop up to 20 times just in case of extreme bad luck
                {
                    // Allocate fresh memory for every single attempt to defeat GDI+ caching
                    IntPtr data = Marshal.AllocCoTaskMem(fontData.Length);
                    Marshal.Copy(fontData, 0, data, fontData.Length);

                    var solo = new PrivateFontCollection();
                    solo.AddMemoryFont(data, fontData.Length);

                    if (solo.Families.Length > 0)
                    {
                        var family = solo.Families[0];

                        // If it's a specific weight (like Medium) but GDI+ named it generic "Montserrat", 
                        // it means it merged. This is a FAILURE. We loop again.
                        if (family.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
                        {
                            _montserratWeights[weight] = family;

                            // Now that we have our isolated pointer, register it for the RichTextBox
                            _fontCollection.AddMemoryFont(data, fontData.Length);
                            uint cFonts = 0;
                            NativeMethods.AddFontMemResourceEx(data, (uint)fontData.Length, IntPtr.Zero, ref cFonts);

                            Debug.WriteLine($"[FontLoad] Key: {weight,-15} | Mapped To: {family.Name,-20} | Status: CORRECT");
                            success = true;
                            // Note: We do NOT free 'data' because the FontFamily and GDI+ need it alive.
                        }
                        else
                        {
                            Debug.WriteLine($"[FontLoad] !! INCORRECT !! Key: {weight} expected '{expectedName}' but GDI+ merged it into '{family.Name}'. Retrying...");

                            // Clean up this failed attempt's memory and try again
                            Marshal.FreeCoTaskMem(data);
                            solo.Dispose();
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[FontLoad] !! FAILURE !! GDI+ failed to load {weight} into memory. Retrying...");

                        Marshal.FreeCoTaskMem(data);
                        solo.Dispose();
                    }
                }
            }
        }

        private static Font GetMontserrat(string weight, float size)
        {
            if (_montserratWeights.TryGetValue(weight, out FontFamily? family))
            {
                // Check if this specific family object supports the Bold style.
                // If we loaded "Montserrat-Bold.ttf", IsStyleAvailable(Bold) will be true.
                if (weight.Contains("Bold") && family.IsStyleAvailable(FontStyle.Bold))
                    return new Font(family, size, FontStyle.Bold);

                if (weight.Contains("Italic") && family.IsStyleAvailable(FontStyle.Italic))
                    return new Font(family, size, FontStyle.Italic);

                return new Font(family, size, FontStyle.Regular);
            }

            return new Font(SystemFonts.DefaultFont.FontFamily, size);
        }

        // Plain text
        public static void AppendText(RichTextBox box, string message)
        {
            Remember(box, "", message, Color.White);
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;

            box.SelectionFont = GetThemeFont();
            box.SelectionColor = box.ForeColor;
            UI.EmojiImages.Append(box, $"{message}\r\n");

            box.SelectionStart = box.TextLength;
            box.ScrollToCaret();
            NativeMethods.HideCaret(box.Handle);
        }

        // Chat message with sender name
        public static void AppendChatMessage(RichTextBox box, string sender, string message)
        {
            Color nameColor = NameColorUtil.GetNameColor(sender);
            Remember(box, sender + ": ", message, nameColor,sender);

            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;

            // name
            box.SelectionFont = GetThemeFont(bold: true);
            box.SelectionColor = nameColor;
            int senderStart=box.TextLength;
            box.AppendText($"{sender}: ");
            RememberSender(box,senderStart,sender.Length,sender);

            // message
            box.SelectionColor = box.ForeColor;
            UI.EmojiImages.Append(box, $"{message}\r\n");

            box.SelectionColor = box.ForeColor;

            box.SelectionStart = box.TextLength;
            box.ScrollToCaret();
            NativeMethods.HideCaret(box.Handle);
        }

        // Whisper message with [To/From] prefix and colored sender name
        public static void AppendWhisperMessage(RichTextBox box, string sender, string target, string text, bool isTo)
        {
            Color nameColor = NameColorUtil.GetNameColor(sender);

            string prefix = isTo
                ? string.Format(Strings.WhisperTo, target)
                : string.Format(Strings.WhisperFrom, sender);
            Remember(box, $"[{prefix}] {sender}: ", text, nameColor,isTo?target:sender);

            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;

            box.SelectionFont = GetThemeFont();
            box.SelectionColor = box.ForeColor;
            box.AppendText($"[{prefix}] ");

            box.SelectionColor = nameColor;
            int senderStart=box.TextLength;
            box.AppendText(sender);
            RememberSender(box,senderStart,sender.Length,sender);

            box.SelectionColor = box.ForeColor;
            UI.EmojiImages.Append(box, $": {text}\r\n");

            box.SelectionStart = box.TextLength;
            box.ScrollToCaret();
            NativeMethods.HideCaret(box.Handle);
        }

        // System message
        public static void AppendSystemMessage(RichTextBox box, string message)
        {
            Remember(box, "", message, Color.White);
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;

            box.SelectionFont = GetThemeFont();
            box.SelectionColor = Color.Gray;
            box.AppendText($"{Strings.System}: ");

            box.SelectionColor = box.ForeColor;
            UI.EmojiImages.Append(box, $"{message}\r\n");

            box.SelectionStart = box.TextLength;
            box.ScrollToCaret();
            NativeMethods.HideCaret(box.Handle);
        }

        // Main overload for broadcast messages
        public static void AppendBroadcastMessage(RichTextBox box, string sender, string message, Color? overrideColor)
        {
            // Use overrideColor if provided, otherwise use NameColorUtil
            Color nameColor = overrideColor ?? NameColorUtil.GetNameColor(sender);
            Remember(box, sender + ": ", message, nameColor,sender);

            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;

            box.SelectionFont = GetThemeFont(bold: true);
            box.SelectionColor = nameColor;
            int senderStart=box.TextLength;
            box.AppendText($"{sender}: ");
            RememberSender(box,senderStart,sender.Length,sender);

            box.SelectionFont = GetThemeFont();
            box.SelectionColor = box.ForeColor;
            UI.EmojiImages.Append(box, $"{message}\r\n");

            box.SelectionStart = box.TextLength;
            box.ScrollToCaret();
            NativeMethods.HideCaret(box.Handle);
        }

        // Hex String Overload
        public static void AppendBroadcastMessage(RichTextBox box, string sender, string message, string? hexColor)
        {
            Color? parsedColor = null;
            if (!string.IsNullOrEmpty(hexColor))
            {
                try
                {
                    parsedColor = ColorTranslator.FromHtml(hexColor);
                }
                catch { /* Invalid hex */ }
            }
            AppendBroadcastMessage(box, sender, message, parsedColor);
        }

        // Default Overload
        public static void AppendBroadcastMessage(RichTextBox box, string sender, string message)
        {
            AppendBroadcastMessage(box, sender, message, (Color?)null);
        }

#if DEBUG
        public static void ShowcasePreview(RichTextBox box)
        {
            AppendSystemMessage(box, string.Format(Strings.ConnectingToServer, "66f27f28-2ca4-4c35-93ed-8002346d4edb"));
            AppendSystemMessage(box, Strings.ConnectedSuccessfully);
            AppendChatMessage(box, "im_riri", "Hello over WS!");
            AppendChatMessage(box, "Guest 41670", "Hello World!");
            AppendChatMessage(box, "Guest 39360", "What's up?");
            AppendSystemMessage(box, string.Format(Strings.EchoResponse, "Hello over HTTP!"));
            AppendSystemMessage(box, Strings.MessageRejectedModeration);
            AppendWhisperMessage(box, "im_riri", "Guest 39360", "Are we ready to launch?", true);
            AppendWhisperMessage(box, "Guest 39360", "im_riri", "Send it!", false);
            AppendText(box, "");
            AppendText(box, "");
            AppendText(box, "");
        }
#endif
    }
}
