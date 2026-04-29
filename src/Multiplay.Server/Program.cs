using Microsoft.EntityFrameworkCore;
using Multiplay.Server;
using Multiplay.Server.Data;
using Multiplay.Server.Features.Auth;
using Multiplay.Server.Infrastructure.Auth;
using Multiplay.Server.Infrastructure.GameState;
using Multiplay.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<ISessionStore,  InMemorySessionStore>();
builder.Services.AddSingleton<IGameState,     InMemoryGameState>();
builder.Services.AddSingleton<ICombatService, CombatService>();
builder.Services.AddSingleton<IEnemyAI,       EnemyAI>();
builder.Services.AddHostedService<GameServer>();

var app = builder.Build();

// Apply pending migrations on startup — retry in case the DB container isn't ready yet
using (var scope = app.Services.CreateScope())
{
    var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

    for (int attempt = 1; ; attempt++)
    {
        try
        {
            db.Database.Migrate();
            break;
        }
        catch (Exception ex) when (attempt < 10)
        {
            logger.LogWarning("Migration attempt {Attempt} failed: {Message}. Retrying in 3 s…",
                attempt, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}

// Auth feature endpoints
var auth = app.MapGroup("/auth");
Register.Map(auth);
Login.Map(auth);
Setup.Map(auth);
PlayerData.Map(auth);

app.MapGet("/health", () => Results.Ok("ok"));

app.Run();
