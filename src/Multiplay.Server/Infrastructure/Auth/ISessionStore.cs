namespace Multiplay.Server.Infrastructure.Auth;

public record SessionInfo(
    int UserId,
    string Username,
    string? DisplayName,
    string? CharacterType,
    int Level,
    int Xp);

public interface ISessionStore
{
    void Set(string token, SessionInfo info);
    bool TryGet(string token, out SessionInfo? info);
    void Remove(string token);
}
