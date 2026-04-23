using Microsoft.AspNetCore.Http.HttpResults;
using Multiplay.Server.Features.Auth;
using Multiplay.Server.Infrastructure.Auth;
using Multiplay.Server.Tests.Helpers;

namespace Multiplay.Server.Tests.Features.Auth;

public class SetupTests
{
    private static async Task<string> RegisterUser(
        string username, string password,
        AppDbContext db, InMemorySessionStore sessions)
    {
        var result = await Register.Handle(new Register.Request(username, password), db, sessions);
        return ((Ok<AuthResponse>)result).Value!.Token;
    }

    [Fact]
    public async Task Setup_WithValidInput_ReturnsOk()
    {
        await using var db = DbHelper.CreateDb();
        var sessions = new InMemorySessionStore();
        var token = await RegisterUser("alice", "password123", db, sessions);

        var result = await Setup.Handle(
            new Setup.Request("Alice", "Zink"),
            token, db, sessions);

        var ok = Assert.IsType<Ok<AuthResponse>>(result);
        Assert.Equal("Alice", ok.Value!.DisplayName);
        Assert.Equal("Zink", ok.Value.CharacterType);
    }

    [Fact]
    public async Task Setup_UpdatesSessionStore()
    {
        await using var db = DbHelper.CreateDb();
        var sessions = new InMemorySessionStore();
        var token = await RegisterUser("alice", "password123", db, sessions);

        await Setup.Handle(new Setup.Request("Alice", "ShieldKnight"), token, db, sessions);

        Assert.True(sessions.TryGet(token, out var info));
        Assert.Equal("Alice", info!.DisplayName);
        Assert.Equal("ShieldKnight", info.CharacterType);
    }

    [Fact]
    public async Task Setup_WithInvalidToken_ReturnsUnauthorized()
    {
        await using var db = DbHelper.CreateDb();
        var sessions = new InMemorySessionStore();

        var result = await Setup.Handle(
            new Setup.Request("Alice", "Zink"),
            "bad-token", db, sessions);

        Assert.IsType<UnauthorizedHttpResult>(result);
    }

    [Fact]
    public async Task Setup_WithNullToken_ReturnsUnauthorized()
    {
        await using var db = DbHelper.CreateDb();
        var sessions = new InMemorySessionStore();

        var result = await Setup.Handle(
            new Setup.Request("Alice", "Zink"),
            null, db, sessions);

        Assert.IsType<UnauthorizedHttpResult>(result);
    }

    [Fact]
    public async Task Setup_WithInvalidCharacterType_ReturnsBadRequest()
    {
        await using var db = DbHelper.CreateDb();
        var sessions = new InMemorySessionStore();
        var token = await RegisterUser("alice", "password123", db, sessions);

        var result = await Setup.Handle(
            new Setup.Request("Alice", "Dragon"),
            token, db, sessions);

        Assert.IsType<BadRequest<string>>(result);
    }

    [Fact]
    public async Task Setup_WithEmptyDisplayName_ReturnsBadRequest()
    {
        await using var db = DbHelper.CreateDb();
        var sessions = new InMemorySessionStore();
        var token = await RegisterUser("alice", "password123", db, sessions);

        var result = await Setup.Handle(
            new Setup.Request("  ", "Zink"),
            token, db, sessions);

        Assert.IsType<BadRequest<string>>(result);
    }

    [Fact]
    public async Task Setup_WithDisplayNameTooLong_ReturnsBadRequest()
    {
        await using var db = DbHelper.CreateDb();
        var sessions = new InMemorySessionStore();
        var token = await RegisterUser("alice", "password123", db, sessions);

        var result = await Setup.Handle(
            new Setup.Request(new string('a', 33), "Zink"),
            token, db, sessions);

        Assert.IsType<BadRequest<string>>(result);
    }
}
