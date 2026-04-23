namespace Multiplay.Client.Services;

public interface IAuthService : IDisposable
{
    int?    UserId        { get; }
    string? Username      { get; }
    string? Token         { get; }
    string? DisplayName   { get; }
    string? CharacterType { get; }

    bool IsLoggedIn  { get; }
    bool IsSetupDone { get; }

    Task<string?> RegisterAsync(string username, string password);
    Task<string?> LoginAsync(string username, string password);
    Task<string?> SetupAsync(string displayName, string characterType);
}
