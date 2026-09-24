using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace RobloxChatLauncher.Services;

internal static class LocalRobloxAccount
{
    internal static string Mask(string text) => text.StartsWith("/cookie ",StringComparison.OrdinalIgnoreCase)
        ? text[..8] + new string('*',text.Length-8) : text;
    private static readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RobloxChatLauncher", "account.dpapi");
    internal static void Save(string value)
    {
        if (value == "clear") { if (File.Exists(path)) File.Delete(path); return; }
        if (value.Length < 40 || value.Any(char.IsWhiteSpace) || value.Contains(';')) throw new ArgumentException("Invalid cookie format.");
        byte[] plain = Encoding.UTF8.GetBytes(value);
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    internal static async Task<string> RequestFriend(long userId)
    {
        if (!File.Exists(path)) return "Set your local authentication using /cookie first. Never share that cookie.";
        byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser);
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
            handler.CookieContainer.Add(new Uri("https://friends.roblox.com"), new Cookie(".ROBLOSECURITY",Encoding.UTF8.GetString(plain),"/") { Secure=true, HttpOnly=true });
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            var url = new Uri($"https://friends.roblox.com/v1/users/{userId}/request-friendship");
            using var first = await client.PostAsync(url,new StringContent("{}",Encoding.UTF8,"application/json"));
            if (first.IsSuccessStatusCode) return "Friend request sent.";
            if (first.StatusCode == HttpStatusCode.Forbidden && first.Headers.TryGetValues("x-csrf-token",out var tokens))
            {
                using var request = new HttpRequestMessage(HttpMethod.Post,url) { Content=new StringContent("{}",Encoding.UTF8,"application/json") };
                request.Headers.Add("x-csrf-token",tokens.First());
                using var response = await client.SendAsync(request);
                if (response.IsSuccessStatusCode) return "Friend request sent.";
            }
            return "Roblox did not accept the request. Check your login or use the Roblox profile page (a challenge may be required).";
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}
