using System.Drawing;

namespace RobloxChatLauncher.UI;

internal static class CoreGui2016Textures
{
    private static readonly Dictionary<string,Image?> Cache=new(StringComparer.OrdinalIgnoreCase);
    internal static Image? Get(string name)
    {
        if(Cache.TryGetValue(name,out var cached))return cached;
        using Stream? stream=typeof(CoreGui2016Textures).Assembly.GetManifestResourceStream($"CoreGui2016.{name}.png");
        if(stream==null)return Cache[name]=null;
        using var source=Image.FromStream(stream);
        return Cache[name]=new Bitmap(source);
    }
}
