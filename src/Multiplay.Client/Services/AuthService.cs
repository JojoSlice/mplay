using System.Net.Http.Json;

namespace Multiplay.Client.Services;

public sealed class AuthService : IAuthService
{
    private readonly HttpClient _http;

    public int?    UserId        { get; private set; }
    public string? Username      { get; private set; }
    public string? Token         { get; private set; }
    public string? DisplayName   { get; private set; }
    public string? CharacterType { get; private set; }

    public bool IsLoggedIn  => Token       is not null;
    public bool IsSetupDone => DisplayName is not null;

    public AuthService(string serverBaseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(serverBaseUrl) };
    }

    public async Task<string?> RegisterAsync(string username, string password)
    {
        try
        {
            var res = await _http.PostAsJsonAsync("auth/register", new { username, password });
            if (!res.IsSuccessStatusCode) return await res.Content.ReadAsStringAsync();
            Apply(await res.Content.ReadFromJsonAsync<AuthResponse>());
            return null;
        }
        catch (Exception ex) { return $"Connection error: {ex.Message}"; }
    }

    public async Task<string?> LoginAsync(string username, string password)
    {
        try
        {
            var res = await _http.PostAsJsonAsync("auth/login", new { username, password });
            if (!res.IsSuccessStatusCode) return await res.Content.ReadAsStringAsync();
            Apply(await res.Content.ReadFromJsonAsync<AuthResponse>());
            return null;
        }
        catch (Exception ex) { return $"Connection error: {ex.Message}"; }
    }

    public async Task<string?> SetupAsync(string displayName, string characterType)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "auth/setup")
            {
                Content = JsonContent.Create(new { displayName, characterType }),
                Headers = { Authorization = new("Bearer", Token) },
            };
            var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return await res.Content.ReadAsStringAsync();
            Apply(await res.Content.ReadFromJsonAsync<AuthResponse>());
            return null;
        }
        catch (Exception ex) { return $"Connection error: {ex.Message}"; }
    }

    private void Apply(AuthResponse? r)
    {
        if (r is null) return;
        UserId        = r.UserId;
        Username      = r.Username;
        Token         = r.Token;
        DisplayName   = r.DisplayName;
        CharacterType = r.CharacterType;
    }

    public void Dispose() => _http.Dispose();

    private sealed record AuthResponse(
        int UserId, string Username, string Token,
        string? DisplayName, string? CharacterType);
}
