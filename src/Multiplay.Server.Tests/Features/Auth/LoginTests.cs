using Microsoft.AspNetCore.Http.HttpResults;
using Multiplay.Server.Features.Auth;
using Multiplay.Server.Infrastructure.Auth;
using Multiplay.Server.Tests.Helpers;

namespace Multiplay.Server.Tests.Features.Auth;

public class LoginTests
{
    private static async Task<string> RegisterUser(
        string username, string password,
        AppDbContext db, InMemorySessionStore sessions)
    {
        var result = await Register.Handle(new Register.Request(username, password), db, sessions);
        return ((Ok<AuthResponse>)result).Value!.Token;
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsOkWithNewToken()
    {
        await using var db = DbHelper.CreateDb();
        var sessions = new InMemorySessionStore();
        var oldToken = await RegisterUser("alice", "password123", db, sessions);

        var result = await Login.Handle(
            new Login.Request("alice", "password123"),
            db, sessions);

        var ok = Assert.IsType<Ok<AuthResponse>>(result);
        Assert.Equal("alice", ok.Value!.Username);
        Assert.NotNull(ok.Value.Token);
        Assert.NotEqual(oldToken, ok.Value.Token); // token is rotated
    }

    [Fact]
    public async Task Login_RotatesToken_OldTokenRemovedFromSessionStore()
    {
        await using var db = DbHelper.CreateDb();
        var sessions = new InMemorySessionStore();
        var oldToken = await RegisterUser("alice", "password123", db, sessions);

        await Login.Handle(new Login.Request("alice", "password123"), db, sessions);

        Assert.False(sessions.TryGet(oldToken, out _));
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        await using var db = DbHelper.CreateDb();
        var sessions = new InMemorySessionStore();
        await RegisterUser("alice", "password123", db, sessions);

        var result = await Login.Handle(
            new Login.Request("alice", "wrong_password"),
            db, sessions);

        Assert.IsType<UnauthorizedHttpResult>(result);
    }

    [Fact]
    public async Task Login_WithUnknownUsername_ReturnsUnauthorized()
    {
        await using var db = DbHelper.CreateDb();

        var result = await Login.Handle(
            new Login.Request("nobody", "password123"),
            db, new InMemorySessionStore());

        Assert.IsType<UnauthorizedHttpResult>(result);
    }

    [Fact]
    public async Task Login_ReturnsDisplayNameAndCharacterType_WhenSetupComplete()
    {
        await using var db = DbHelper.CreateDb();
        var sessions = new InMemorySessionStore();
        var token = await RegisterUser("alice", "password123", db, sessions);

        await Setup.Handle(
            new Setup.Request("Alice In Wonderland", "Zink"),
            token, db, sessions);

        var result = await Login.Handle(
            new Login.Request("alice", "password123"),
            db, sessions);

        var ok = Assert.IsType<Ok<AuthResponse>>(result);
        Assert.Equal("Alice In Wonderland", ok.Value!.DisplayName);
        Assert.Equal("Zink", ok.Value.CharacterType);
    }
}
