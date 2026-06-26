# Conquer Online 5065 Private Server

A modernized Conquer Online patch 5065 private server based on COServer Redux, rebuilt for .NET 8 and MySQL 8.

## Requirements

- **Docker Desktop** (with WSL 2 on Windows, or Linux/macOS)
- **CO 5065 client** (patch 5065 — you must supply your own client)
- **Map files** — place `.cqmap` files in `src/maps/` (operator must supply)

## Getting Started

```bash
git clone https://github.com/vitorguima/conquer-server
docker compose up
```

Point your CO 5065 client to `127.0.0.1:9958` (auth server).

## Configuration

| Environment Variable | Default | Description |
|---|---|---|
| `ConnectionStrings__Default` | (see docker-compose.yml) | MySQL connection string |
| `AuthPort` | `9958` | Auth server TCP port |
| `GamePort` | `5816` | Game server TCP port |
| `GameServer__Ip` | `127.0.0.1` | IP sent to client for game server |

## Ports

| Port | Protocol | Purpose |
|---|---|---|
| 9958 | TCP | Auth server (CO client connects here first) |
| 5816 | TCP | Game server (redirected to after auth) |
| 3306 | TCP | MySQL (internal, not exposed externally) |

## Map Files

The server loads `.cqmap` binary map files from `src/maps/`. These are not included in the repository — operators must supply them from the original CO 5065 game files.

## Development

```bash
# Build
cd src/Redux
dotnet build

# Run locally (no Docker, DB must be running separately)
dotnet run

# Publish
dotnet publish -c Release -o ./publish
```

## Architecture

- **Auth flow**: Client → port 9958 → TQCipher + RC5 decrypt → account lookup → token issued → redirect to game port
- **Game flow**: Client → port 5816 → token consumed → TQCipher K2 activated → character loaded
- **Stack**: .NET 8, C#, Dapper, MySqlConnector, MySQL 8
