# Live Leaderboard

A real multi-competition leaderboard built to make one architecture visible:

`SQL Server → outbox → worker → Redis sorted set → SignalR → browsers`

This is a standalone project at `C:\Users\josh3\Projects\live-leaderboard`. It does not use The House project or its database.

## What each technology is doing

- SQL Server is the durable system of record. It stores competitions, participants, immutable score events, current totals, Identity users, and projection work.
- Redis is the fast read model. Each competition has a sorted set named `leaderboard:{competitionId}`. The member is a participant id and the score is that participant's absolute total. `ZREVRANGE` gives highest scores first.
- The outbox closes the reliability gap between SQL and Redis. A committed score creates an outbox row in the same SQL transaction, so a Redis outage does not lose the score.
- The background worker claims outbox rows, writes absolute totals with `ZADD`, reads a complete Redis snapshot, broadcasts it, and marks the row published. Retries are safe because the Redis write is absolute, not an increment.
- SignalR sends a complete replacement snapshot to the group for one competition. Every open public page for that competition re-renders without polling or refreshing.

## Prerequisites

- .NET 10 SDK
- SQL Server available at `localhost\SQL` (the default database is `LiveLeaderboard`)
- Docker Desktop for Redis, or a Redis server reachable at `localhost:6379`

The database name is deliberately separate from The House database.

## Start the application

From this directory:

```powershell
docker compose up -d redis
dotnet ef database update --project src/LiveLeaderboard.Data --startup-project src/LiveLeaderboard.Web
dotnet run --project src/LiveLeaderboard.Web
```

Open the URL printed by ASP.NET Core. The public home page lists all Live and Completed competitions.

If Docker Desktop's Linux engine is unavailable, Redis can still run locally through WSL2. The project was verified against Redis 7 this way. The simplest option is to install Redis in your Ubuntu distro and start it on port 6379; no application code changes are required. Verify the endpoint with:

```powershell
wsl -d Ubuntu -- redis-cli -h 127.0.0.1 -p 6379 ping
```

The expected response is `PONG`. Docker Compose remains the reproducible default and keeps data in the named `live-leaderboard-redis-data` volume.

## Optional development users and seed data

The default configuration does not create users or competitions. To seed a Draft competition and development users, store credentials in user-secrets or environment variables; do not commit them:

```powershell
dotnet user-secrets --project src/LiveLeaderboard.Web set SeedDemoData true
dotnet user-secrets --project src/LiveLeaderboard.Web set Seed:AdminEmail admin@example.test
dotnet user-secrets --project src/LiveLeaderboard.Web set Seed:AdminPassword "use-a-local-password"
dotnet user-secrets --project src/LiveLeaderboard.Web set Seed:ScorerEmail scorer@example.test
dotnet user-secrets --project src/LiveLeaderboard.Web set Seed:ScorerPassword "use-another-local-password"
```

After restarting the app:

1. Sign in as the admin.
2. Open `/admin/competitions` and move the seeded competition to Live.
3. Open `/scoring` as the scorer and record a signed score adjustment.
4. Open the public board in two browser tabs and watch both reorder together.

The scorer UI never writes Redis directly. It calls the transactional score service; the worker performs the projection.

## Inspect the Redis sorted set

Find the competition id in SQL Server or the admin UI, then run:

```powershell
redis-cli -h localhost -p 6379 ZREVRANGE leaderboard:<competition-id> 0 -1 WITHSCORES
redis-cli -h localhost -p 6379 ZCARD leaderboard:<competition-id>
```

The ids are stored in Redis as compact `N`-format GUIDs. The score is the absolute `ParticipantTotal`, not the latest points adjustment.

## Demonstrate Redis outage recovery

With the app running, stop only Redis:

```powershell
docker compose stop redis
```

Submit a score from `/scoring`. SQL Server still accepts the event, updates the total, and creates a Pending/Processing outbox row. The public page shows that the projection is catching up; it does not fall back to a SQL ranking query.

Bring Redis back:

```powershell
docker compose start redis
```

The worker retries the outbox row, writes the absolute total, broadcasts the new snapshot through SignalR, and marks the work Published. For a deliberate full rebuild, use the admin Projection health page; it writes a temporary sorted set and atomically swaps it into the canonical key.

## Tests

```powershell
dotnet restore LiveLeaderboard.slnx
dotnet test LiveLeaderboard.slnx
dotnet build LiveLeaderboard.slnx --no-restore
```

The integration tests use the separate `LiveLeaderboard_Test` SQL database and the Redis endpoint at `localhost:6379`.

The real Chromium smoke test is opt-in because it starts a Kestrel process and creates test data:

```powershell
dotnet build LiveLeaderboard.slnx --no-restore
pwsh tests/LiveLeaderboard.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
$env:LIVE_LEADERBOARD_RUN_BROWSER_TEST = "1"
dotnet test tests/LiveLeaderboard.Tests/LiveLeaderboard.Tests.csproj --filter FullyQualifiedName~LiveLeaderboardPlaywrightTests
```

It opens two public pages, logs in through the scorer UI, submits a real score, and asserts both pages converge on the new order without a reload.

## Architecture notes

See [docs/architecture/redis-signalr-data-flow.md](docs/architecture/redis-signalr-data-flow.md) for the request flow, failure paths, and the learning checkpoints for SQL Server, Redis sorted sets, the outbox, the worker, and SignalR.
