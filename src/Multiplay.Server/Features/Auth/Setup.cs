using Microsoft.EntityFrameworkCore;
using Multiplay.Server.Data;
using Multiplay.Server.Infrastructure.Auth;
using Multiplay.Shared;

namespace Multiplay.Server.Features.Auth;

public static class Setup
{
    public record Request(string DisplayName, string CharacterType);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("setup",
            (Request req, HttpContext http, AppDbContext db, ISessionStore sessions) =>
                Handle(req, ExtractBearerToken(http), db, sessions));

    internal static async Task<IResult> Handle(
        Request req, string? token, AppDbContext db, ISessionStore sessions)
    {
        if (string.IsNullOrEmpty(token)
            || !sessions.TryGet(token, out var info)
            || info is null)
            return Results.Unauthorized();

        var displayName = req.DisplayName.Trim();
        if (displayName.Length == 0 || displayName.Length > 32)
            return Results.BadRequest("Display name must be 1–32 characters.");

        if (!CharacterType.IsValid(req.CharacterType))
            return Results.BadRequest($"Invalid character type '{req.CharacterType}'.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.SessionToken == token);
        if (user is null) return Results.Unauthorized();

        user.DisplayName   = displayName;
        user.CharacterType = req.CharacterType;
        await db.SaveChangesAsync();

        sessions.Set(token, info with { DisplayName = displayName, CharacterType = req.CharacterType });

        return Results.Ok(new AuthResponse(
            user.Id, user.Username, token,
            user.DisplayName, user.CharacterType));
    }

    private static string? ExtractBearerToken(HttpContext http) =>
        http.Request.Headers.Authorization
            .FirstOrDefault()
            ?.Split(' ', 2)
            .LastOrDefault();
}
