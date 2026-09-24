using System.Drawing;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace RobloxChatLauncher.UI;

internal sealed class EmojiPicker : Form
{
    internal record Entry(string Emoji, string Alias, string Search);
    private static readonly List<Entry> catalog = Load();
    private readonly ListBox list = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
        BackColor = Color.FromArgb(35, 37, 39), ForeColor = Color.White, ItemHeight = 32, DrawMode = DrawMode.OwnerDrawFixed };
    private List<Entry> matches = new();
    private string query = "";
    internal event Action<string>? Chosen;
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80; return cp; } }
    public EmojiPicker()
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; Controls.Add(list);
        list.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= matches.Count) return;
            using var bg = new SolidBrush(e.Index == list.SelectedIndex ? Color.FromArgb(70, 73, 77) : list.BackColor);
            e.Graphics.FillRectangle(bg, e.Bounds);
            EmojiImages.Draw(e.Graphics, matches[e.Index].Emoji, new Rectangle(8, e.Bounds.Y + 4, 24, 24));
            TextRenderer.DrawText(e.Graphics, ":" + matches[e.Index].Alias + ":", SystemFonts.MessageBoxFont,
                new Point(48, e.Bounds.Y + 8), Color.White);
        };
        list.MouseClick += (_, e) => { int i = list.IndexFromPoint(e.Location); if (i >= 0) { list.SelectedIndex = i; Accept(); } };
    }
    private static List<Entry> Load()
    {
        using var stream = typeof(EmojiPicker).Assembly.GetManifestResourceStream("emoji.json");
        if (stream == null) return new();
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.EnumerateArray().SelectMany(item => item.GetProperty("aliases").EnumerateArray()
            .Select(alias => new Entry(item.GetProperty("emoji").GetString()!, alias.GetString()!,
                item.GetProperty("description").GetString()! + " " + item.GetProperty("tags").ToString()))).ToList();
    }
    internal static List<Entry> Search(string value) => catalog
        .Where(e => EmojiImages.HasImage(e.Emoji) && (e.Alias.Contains(value, StringComparison.OrdinalIgnoreCase) || e.Search.Contains(value, StringComparison.OrdinalIgnoreCase)))
        .OrderByDescending(e => e.Alias.StartsWith(value, StringComparison.OrdinalIgnoreCase)).ThenBy(e => e.Alias.Length)
        .GroupBy(e => e.Emoji).Select(g => g.First()).Take(8).ToList();
    internal static bool IsEmoji(string value) => EmojiImages.HasImage(value) || value.EnumerateRunes().Any(r => r.Value is >= 0x1F000 and <= 0x1FAFF || r.Value is >= 0x2600 and <= 0x27ff || r.Value is 0xfe0f or 0x20e3);
    internal static IEnumerable<string> Runs(string value)
    {
        string buffer = ""; bool? emoji = null;
        foreach (string element in System.Globalization.StringInfo.GetTextElementEnumerator(value).AsElements())
        {
            bool next = IsEmoji(element);
            if (emoji != null && (emoji != next || next)) { yield return buffer; buffer = ""; }
            buffer += element; emoji = next;
        }
        if (buffer.Length > 0) yield return buffer;
    }
    internal static Match Token(string input, int caret) => Regex.Match(input[..Math.Clamp(caret, 0, input.Length)], @"(?<![\w:]):([a-zA-Z0-9_+\-]+)$");
    internal static string Expand(string input) => Regex.Replace(input, @":([a-zA-Z0-9_+\-]+):", m => catalog.FirstOrDefault(e => e.Alias.Equals(m.Groups[1].Value, StringComparison.OrdinalIgnoreCase))?.Emoji ?? m.Value);
    internal void UpdateSuggestions(string input, int caret, Rectangle bar, bool enabled)
    {
        var token = Token(input, caret);
        if (!enabled || !token.Success) { query = ""; Hide(); return; }
        string value = token.Groups[1].Value;
        if (query != value)
        {
            query = value; matches = Search(value);
            list.Items.Clear(); foreach (var match in matches) list.Items.Add(match.Alias);
            if (matches.Count > 0) list.SelectedIndex = 0;
        }
        if (matches.Count == 0) { Hide(); return; }
        var work = Screen.FromRectangle(bar).WorkingArea;
        Size = new Size(Math.Min(320, work.Width), matches.Count * 32);
        int y = bar.Bottom + 3;
        if (y + Height > work.Bottom) y = bar.Top - Height - 3;
        Location = new Point(Math.Clamp(bar.Left, work.Left, work.Right - Width), Math.Clamp(y, work.Top, work.Bottom - Height));
        if (!Visible) Show();
    }
    internal bool HandleKey(Keys key)
    {
        if (!Visible) return false;
        if (key == Keys.Escape) { Hide(); return true; }
        if (key is Keys.Tab or Keys.Enter) { Accept(); return true; }
        if (key is Keys.Up or Keys.Down)
        {
            list.SelectedIndex = (list.SelectedIndex + (key == Keys.Up ? matches.Count - 1 : 1)) % matches.Count;
            return true;
        }
        return false;
    }
    private void Accept() { if (list.SelectedIndex >= 0) Chosen?.Invoke(matches[list.SelectedIndex].Emoji); Hide(); }
}

internal static class TextElements
{
    internal static IEnumerable<string> AsElements(this System.Globalization.TextElementEnumerator enumerator)
    { while (enumerator.MoveNext()) yield return enumerator.GetTextElement(); }
}
