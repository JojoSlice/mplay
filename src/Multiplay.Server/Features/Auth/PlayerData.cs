using Microsoft.EntityFrameworkCore;
using Multiplay.Server.Data;
using Multiplay.Server.Infrastructure.Auth;

namespace Multiplay.Server.Features.Auth;

/// <summary>Saves persistent player progression data (weapon choice, quest completion).</summary>
public static class PlayerData
{
    public record Request(string? WeaponType, bool? SlimeQuestDone);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPatch("player-data",
            (Request req, HttpContext http, AppDbContext db, ISessionStore sessions) =>
                Handle(req, AuthHelpers.ExtractBearerToken(http), db, sessions));

    private static readonly string[] ValidWeapons = ["Sword", "Bow", "Wand"];

    internal static async Task<IResult> Handle(
        Request req, string? token, AppDbContext db, ISessionStore sessions)
    {
        if (string.IsNullOrEmpty(token) || !sessions.TryGet(token, out var info) || info is null)
            return Results.Unauthorized();

        if (req.WeaponType is not null && !ValidWeapons.Contains(req.WeaponType))
            return Results.BadRequest($"Invalid weapon type '{req.WeaponType}'.");

        var user = await db.Users.FindAsync(info!.UserId);
        if (user is null) return Results.Unauthorized();

        if (req.WeaponType is not null)
            user.WeaponType = req.WeaponType;

        if (req.SlimeQuestDone is true)
            user.SlimeQuestDone = true;

        await db.SaveChangesAsync();

        return Results.Ok(new AuthResponse(
            user.Id, user.Username, token,
            user.DisplayName, user.CharacterType,
            user.WeaponType, user.SlimeQuestDone));
    }
}
