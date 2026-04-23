using Microsoft.AspNetCore.Http.HttpResults;
using Multiplay.Server.Features.Auth;
using Multiplay.Server.Infrastructure.Auth;
using Multiplay.Server.Tests.Helpers;

namespace Multiplay.Server.Tests.Features.Auth;

public class RegisterTests
{
    private static InMemorySessionStore Sessions() => new();

    [Fact]
    public async Task Register_WithValidCredentials_ReturnsOkWithToken()
    {
        await using var db = DbHelper.CreateDb();

        var result = await Register.Handle(
            new Register.Request("alice", "password123"),
            db, Sessions());

        var ok = Assert.IsType<Ok<AuthResponse>>(result);
        Assert.Equal("alice", ok.Value!.Username);
        Assert.NotNull(ok.Value.Token);
        Assert.Null(ok.Value.DisplayName);
        Assert.Null(ok.Value.CharacterType);
    }

    [Fact]
    public async Task Register_SetsSessionStoreEntry()
    {
        await using var db = DbHelper.CreateDb();
        var sessions = Sessions();

        var result = await Register.Handle(
            new Register.Request("alice", "password123"),
            db, sessions);

        var ok = Assert.IsType<Ok<AuthResponse>>(result);
        Assert.True(sessions.TryGet(ok.Value!.Token, out var info));
        Assert.Equal("alice", info!.Username);
    }

    [Fact]
    public async Task Register_WithDuplicateUsername_ReturnsConflict()
    {
        await using var db = DbHelper.CreateDb();
        await Register.Handle(new Register.Request("alice", "password123"), db, Sessions());

        var result = await Register.Handle(
            new Register.Request("alice", "different_password"),
            db, Sessions());

        Assert.IsType<Conflict<string>>(result);
    }

    [Fact]
    public async Task Register_WithShortPassword_ReturnsBadRequest()
    {
        await using var db = DbHelper.CreateDb();

        var result = await Register.Handle(
            new Register.Request("alice", "abc"),
            db, Sessions());

        Assert.IsType<BadRequest<string>>(result);
    }

    [Fact]
    public async Task Register_WithEmptyUsername_ReturnsBadRequest()
    {
        await using var db = DbHelper.CreateDb();

        var result = await Register.Handle(
            new Register.Request("", "password123"),
            db, Sessions());

        Assert.IsType<BadRequest<string>>(result);
    }

    [Fact]
    public async Task Register_WithUsernameTooLong_ReturnsBadRequest()
    {
        await using var db = DbHelper.CreateDb();

        var result = await Register.Handle(
            new Register.Request(new string('a', 33), "password123"),
            db, Sessions());

        Assert.IsType<BadRequest<string>>(result);
    }
}
