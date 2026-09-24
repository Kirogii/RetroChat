using System.Drawing;
using System.Net.Http;
using System.Text.Json;

namespace RobloxChatLauncher.UI;

// UI-thread owned cache. Public thumbnail requests never use the account/cookie client.
internal sealed class CoreGuiAvatars : IDisposable
{
    private readonly HttpClient client=new(new HttpClientHandler { UseCookies=false,AllowAutoRedirect=false }) { Timeout=TimeSpan.FromSeconds(12),MaxResponseContentBufferSize=2*1024*1024 };
    private readonly Dictionary<long,Bitmap> images=new();
    private readonly Dictionary<long,long> attempted=new();
    private readonly HashSet<long> pending=new();
    private readonly CancellationTokenSource lifetime=new();
    private readonly SemaphoreSlim slots=new(2);
    private bool disposed;
    internal event Action? Changed;
    internal Image? Get(ChatMember member,bool request)
    {
        if(!long.TryParse(member.RobloxId,out long id)||id<=0)return CoreGui2016Textures.Get("GuestAvatar");
        if(images.TryGetValue(id,out var image))return image;
        if(request&&!disposed&&!pending.Contains(id)&&pending.Count<128&&(!attempted.TryGetValue(id,out var time)||Environment.TickCount64-time>60000))
        {
            pending.Add(id);attempted[id]=Environment.TickCount64;_ = Fetch(id);
        }
        return CoreGui2016Textures.Get("GuestAvatar");
    }
    private async Task Fetch(long id)
    {
        bool entered=false;
        try
        {
            await slots.WaitAsync(lifetime.Token);entered=true;
            using var response=await client.GetAsync($"https://thumbnails.roblox.com/v1/users/avatar?userIds={id}&size=150x150&format=Png&isCircular=false",lifetime.Token);
            response.EnsureSuccessStatusCode();
            using var data=JsonDocument.Parse(await response.Content.ReadAsStringAsync(lifetime.Token));
            var item=data.RootElement.GetProperty("data").EnumerateArray().FirstOrDefault();
            if(item.ValueKind!=JsonValueKind.Object||item.GetProperty("state").GetString()!="Completed")return;
            if(!Uri.TryCreate(item.GetProperty("imageUrl").GetString(),UriKind.Absolute,out var uri)||uri.Scheme!="https"||!uri.Host.EndsWith(".rbxcdn.com",StringComparison.OrdinalIgnoreCase))return;
            byte[] bytes=await client.GetByteArrayAsync(uri,lifetime.Token);
            using var stream=new MemoryStream(bytes);using var source=Image.FromStream(stream);
            if(disposed||source.Width>1024||source.Height>1024)return;
            var bitmap=new Bitmap(source);
            if(images.Count>=128){long oldest=images.Keys.First();images[oldest].Dispose();images.Remove(oldest);}
            images[id]=bitmap;
            Changed?.Invoke();
        }
        catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException) { /* Keep fallback and retry after cooldown. */ }
        finally {pending.Remove(id);if(entered)slots.Release();}
    }
    public void Dispose(){disposed=true;lifetime.Cancel();client.Dispose();foreach(var image in images.Values)image.Dispose();images.Clear();}
}
