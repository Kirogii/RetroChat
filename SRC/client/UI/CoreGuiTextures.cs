using System.Drawing;
namespace RobloxChatLauncher.UI;
internal static class CoreGuiTextures
{
    private static readonly Dictionary<string,Image?> cache = new();
    internal static Image? Get(string id)
    {
        if (cache.TryGetValue(id,out var found)) return found;
        using var stream = typeof(CoreGuiTextures).Assembly.GetManifestResourceStream($"CoreGui2013.{id}.png");
        try { return cache[id] = stream == null ? null : new Bitmap(stream); }
        catch (ArgumentException) { return cache[id] = null; }
    }
}
