# Multiplay

A real-time multiplayer 2D game built with MonoGame (client) and ASP.NET Core (server).

## Stack

| Layer | Technology |
|---|---|
| Client | MonoGame 3.8 DesktopGL, .NET 9 |
| Server | ASP.NET Core, .NET 10 |
| Networking | LiteNetLib 2.1.3 (UDP) |
| Database | PostgreSQL 16 + EF Core + Npgsql |
| Auth | BCrypt password hashing, session tokens |
| Maps | Tiled (.tmx) via TiledCS |

## Project Structure

```
Multiplay.slnx
├── src/
│   ├── Multiplay.Client/       # MonoGame DesktopGL game
│   │   ├── Content/            # MGCB pipeline (textures, fonts, maps)
│   │   ├── Graphics/           # Animators (Zink, Knight, base CharacterAnimator)
│   │   ├── Network/            # LiteNetLib client wrapper
│   │   ├── Screens/            # Screen manager + all game screens
│   │   ├── Services/           # AuthService (HTTP)
│   │   ├── UI/                 # TextInput, Button
│   │   └── World/              # TileMapRenderer
│   ├── Multiplay.Server/       # ASP.NET Core + game server
│   │   ├── Controllers/        # AuthController (register/login/setup)
│   │   ├── Data/               # EF Core DbContext + migrations
│   │   ├── Models/             # User, Player
│   │   └── GameServer.cs       # LiteNetLib hosted service
│   └── Multiplay.Shared/       # Packets, CharacterType constants
├── docker-compose.yml
└── Legend_of_Zink_Asset_Pack/  # Source art assets
```

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9) (client, shared)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10) (server)
- [Docker + Docker Compose](https://docs.docker.com/get-docker/) (database)
- [MonoGame Content Builder (MGCB Editor)](https://docs.monogame.net/articles/tools/mgcb_editor.html) — optional, for editing content

> On Linux you also need the MonoGame native dependencies:
> ```bash
> sudo apt install libsdl2-dev libopenal-dev
> ```

## Getting Started

### 1. Start the database

```bash
docker compose up db -d
```

### 2. Run the server

The server applies EF migrations automatically on startup.

```bash
dotnet run --project src/Multiplay.Server
```

Server listens on `http://localhost:5000`.  
Game server (UDP) listens on port `9050`.

### 3. Run the client

```bash
dotnet run --project src/Multiplay.Client
```

## Configuration

Server connection string is in `src/Multiplay.Server/appsettings.json`:

```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Database=multiplay;Username=postgres;Password=postgres"
}
```

For local overrides create `src/Multiplay.Server/appsettings.Development.json` (already gitignored).

Client server address is hardcoded in `src/Multiplay.Client/Screens/GameScreen.cs`:

```csharp
private const string ServerHost = "127.0.0.1";
private const int    ServerPort = 9050;
```

## Database Migrations

```bash
# Add a new migration
dotnet ef migrations add <MigrationName> \
  --project src/Multiplay.Server \
  --startup-project src/Multiplay.Server

# Apply manually (normally runs automatically on startup)
dotnet ef database update \
  --project src/Multiplay.Server \
  --startup-project src/Multiplay.Server
```

## Running with Docker (server + db)

```bash
docker compose up --build
```

The server container connects to the `db` service automatically.

## Game Flow

```
StartScreen
  ├── LoginScreen   ──┐
  └── RegisterScreen ─┤
                      ├─ IsSetupDone? ──No──▶ CharacterSelectScreen ──▶ GameScreen
                                      └─Yes─▶ GameScreen
```

**First login:** Players choose a display name and character (Zink, Shield Knight, or Sword Knight) before entering the game world.

## Characters

| ID | Sprite size | Animations |
|---|---|---|
| `Zink` | 16×48 px | Walk, SwordAttack, ClassicSwordAttack, BowAttack, WandAttack, Jump, Death |
| `ShieldKnight` | 16×32 px | Walk (4 directions) |
| `SwordKnight` | 16×32 px | Walk (4 directions) |

Character animators live in `src/Multiplay.Client/Graphics/`. Use the factory to create the right one:

```csharp
var animator = CharacterAnimator.Create(characterType);
```

## Networking

- HTTP (port 5000): auth endpoints (`/auth/register`, `/auth/login`, `/auth/setup`)
- UDP (port 9050): real-time game traffic via LiteNetLib
- Auth token from HTTP login is passed as the LiteNetLib connection key and validated server-side in `OnConnectionRequest`
- Packet types are defined in `src/Multiplay.Shared/Packets.cs`

## Content Pipeline

Sprites and fonts are compiled via the MonoGame Content Pipeline. Source assets live in `src/Multiplay.Client/Content/`. The `.mgcb` file lists all assets; the pipeline runs automatically during build.

To add a new texture, add an entry to `Content.mgcb` or open it in the MGCB Editor.
