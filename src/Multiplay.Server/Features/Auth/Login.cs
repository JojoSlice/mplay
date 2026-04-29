using Microsoft.EntityFrameworkCore;
using Multiplay.Server.Data;
using Multiplay.Server.Infrastructure.Auth;

namespace Multiplay.Server.Features.Auth;

public static class Login
{
    public record Request(string Username, string Password);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("login", Handle);

    internal static async Task<IResult> Handle(
        Request req, AppDbContext db, ISessionStore sessions)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == req.Username);

        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Results.Unauthorized();

        // Rotate token on every login
        if (user.SessionToken is not null)
            sessions.Remove(user.SessionToken);

        var token = TokenGenerator.Generate();
        user.SessionToken = token;
        await db.SaveChangesAsync();

        sessions.Set(token, new SessionInfo(user.Id, user.Username, user.DisplayName, user.CharacterType));

        return Results.Ok(new AuthResponse(
            user.Id, user.Username, token,
            user.DisplayName, user.CharacterType,
            user.WeaponType, user.SlimeQuestDone));
    }
}
