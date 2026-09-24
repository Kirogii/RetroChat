using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace RobloxChatLauncher.UI;

internal static class EmojiImages
{
    private static readonly Assembly assembly = typeof(EmojiImages).Assembly;
    private static readonly HashSet<string> resources = assembly.GetManifestResourceNames().ToHashSet();
    private static readonly Dictionary<string, Image> images = new();
    private static readonly Dictionary<string, string> rtf = new();
    private static string Resource(string emoji) => "EmojiImages." + string.Join("-", emoji.EnumerateRunes().Select(r => r.Value.ToString("x"))) + ".png";
    internal static bool HasImage(string emoji) => resources.Contains(Resource(emoji));
    internal static Image? Get(string emoji)
    {
        string resource = Resource(emoji);
        if (images.TryGetValue(resource, out var image)) return image;
        using var stream = assembly.GetManifestResourceStream(resource);
        if (stream == null) return null;
        using var loaded = Image.FromStream(stream);
        image = new Bitmap(loaded); images[resource] = image;
        return image;
    }
    internal static bool Draw(Graphics graphics, string emoji, RectangleF bounds)
    {
        var image = Get(emoji);
        if (image == null) return false;
        graphics.DrawImage(image, bounds);
        return true;
    }
    internal static void Append(RichTextBox box, string text)
    {
        foreach (string run in EmojiPicker.Runs(text))
        {
            string resource = Resource(run);
            if (!resources.Contains(resource)) { box.AppendText(run); continue; }
            if(!rtf.TryGetValue(resource,out string? picture)) {
                using var stream=assembly.GetManifestResourceStream(resource)!;
                using var bytes=new MemoryStream();stream.CopyTo(bytes);
                picture="{\\rtf1\\ansi{\\pict\\pngblip\\picw72\\pich72\\picwgoal270\\pichgoal270 "+Convert.ToHexString(bytes.ToArray())+"}}";
                rtf[resource]=picture;
            }
            var font = box.SelectionFont; var color = box.SelectionColor;
            box.SelectedRtf = picture;
            box.Select(box.TextLength, 0); box.SelectionFont = font; box.SelectionColor = color;
        }
    }
}
