namespace Multiplay.Server.Features.Auth;

/// <summary>Common response shape returned by all three auth endpoints.</summary>
public record AuthResponse(
    int UserId,
    string Username,
    string Token,
    string? DisplayName,
    string? CharacterType);
