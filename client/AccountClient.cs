using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AfkClient;

internal sealed record AccountResult(bool Ok, string Name, string Message);

internal static class AccountClient
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<AccountResult> RegisterAsync(LauncherConfig config, string name, string password, string password2)
        => await SendAsync(config, "api/public/register", name, password, password2);

    public static async Task<AccountResult> LoginAsync(LauncherConfig config, string name, string password)
        => await SendAsync(config, "api/public/downloads", name, password, password);

    static async Task<AccountResult> SendAsync(LauncherConfig config, string path, string name, string password, string password2)
    {
        string portal = (config?.PortalUrl ?? "").TrimEnd('/');
        if (portal.Length == 0)
            return new AccountResult(false, "", "This launcher has no portal address. Ask the server owner for a new package.");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(password))
            return new AccountResult(false, "", "Enter your account name and password.");
        try
        {
            var body = new { name = name.Trim(), password, password2 };
            using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(portal + "/" + path, content);
            string text = await response.Content.ReadAsStringAsync();
            string error = null, account = null;
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("name", out var n)) account = n.GetString();
                if (doc.RootElement.TryGetProperty("error", out var e)) error = e.GetString();
                if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
                    error ??= "The server rejected the request.";
            }
            catch { }
            if (!response.IsSuccessStatusCode)
                return new AccountResult(false, "", string.IsNullOrWhiteSpace(error)
                    ? "The server could not complete the request (" + (int)response.StatusCode + ")."
                    : error);
            string resultName = string.IsNullOrWhiteSpace(account) ? name.Trim().ToUpperInvariant() : account;
            return new AccountResult(true, resultName, path.EndsWith("register", StringComparison.Ordinal)
                ? "Account created. Use this name and password when you log in to the game."
                : "Logged in. Use this account when you log in to the game.");
        }
        catch (Exception ex)
        {
            return new AccountResult(false, "", ex.Message);
        }
    }
}
