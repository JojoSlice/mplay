using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Multiplay.Server.Data;
using Multiplay.Server.Models;
using Multiplay.Shared;

namespace Multiplay.Server.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController(AppDbContext db) : ControllerBase
{
    public record RegisterRequest(string Username, string Password);
    public record LoginRequest(string Username, string Password);
    public record SetupRequest(string DisplayName, string CharacterType);

    /// <summary>
    /// DisplayName is null when the player hasn't completed character setup yet.
    /// The client should navigate to the setup screen in that case.
    /// </summary>
    public record AuthResponse(int UserId, string Username, string Token, string? DisplayName, string? CharacterType);

    // ── POST /auth/register ────────────────────────────────────────────────────

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || req.Username.Length > 32)
            return BadRequest("Username must be 1–32 characters.");

        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6)
            return BadRequest("Password must be at least 6 characters.");

        if (await db.Users.AnyAsync(u => u.Username == req.Username))
            return Conflict("Username is already taken.");

        var token = GenerateToken();
        var user  = new User
        {
            Username     = req.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            SessionToken = token,
            CreatedAt    = DateTime.UtcNow,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return Ok(new AuthResponse(user.Id, user.Username, token, null, null));
    }

    // ── POST /auth/login ───────────────────────────────────────────────────────

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == req.Username);

        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized("Invalid username or password.");

        user.SessionToken = GenerateToken();
        await db.SaveChangesAsync();

        return Ok(new AuthResponse(user.Id, user.Username, user.SessionToken!,
            user.DisplayName, user.CharacterType));
    }

    // ── POST /auth/setup ───────────────────────────────────────────────────────

    [HttpPost("setup")]
    public async Task<IActionResult> Setup(SetupRequest req)
    {
        // Token passed as Bearer header
        var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
        var user  = await db.Users.FirstOrDefaultAsync(u => u.SessionToken == token);
        if (user is null) return Unauthorized();

        var displayName = req.DisplayName.Trim();
        if (displayName.Length == 0 || displayName.Length > 32)
            return BadRequest("Display name must be 1–32 characters.");

        var validTypes = new[] { CharacterType.Zink, CharacterType.ShieldKnight, CharacterType.SwordKnight };
        if (!validTypes.Contains(req.CharacterType))
            return BadRequest("Invalid character type.");

        user.DisplayName   = displayName;
        user.CharacterType = req.CharacterType;
        await db.SaveChangesAsync();

        return Ok(new AuthResponse(user.Id, user.Username, user.SessionToken!,
            user.DisplayName, user.CharacterType));
    }

    // ── Internal ───────────────────────────────────────────────────────────────

    private static string GenerateToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
               .Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
