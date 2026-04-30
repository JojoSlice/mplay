using Microsoft.EntityFrameworkCore;
using Multiplay.Server.Data;
using Multiplay.Server.Infrastructure.Auth;
using Multiplay.Server.Models;

namespace Multiplay.Server.Features.Auth;

public static class Register
{
    public record Request(string Username, string Password);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("register", Handle);

    internal static async Task<IResult> Handle(
        Request req, AppDbContext db, ISessionStore sessions)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || req.Username.Length > 32)
            return Results.BadRequest("Username must be 1–32 characters.");

        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6)
            return Results.BadRequest("Password must be at least 6 characters.");

        if (await db.Users.AnyAsync(u => u.Username == req.Username))
            return Results.Conflict("Username is already taken.");

        var token = TokenGenerator.Generate();
        var user  = new User
        {
            Username     = req.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            SessionToken = token,
            CreatedAt    = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        sessions.Set(token, new SessionInfo(user.Id, user.Username, null, null, 0, 0));

        return Results.Ok(new AuthResponse(user.Id, user.Username, token, null, null, null, false, 0, 0));
    }
}
