# Live Leaderboard

A real-time competition leaderboard built to demonstrate a durable SQL Server write path, Redis sorted sets for ranking, and SignalR updates for public viewers.

The application lives independently from The House at `C:\Users\josh3\Projects\live-leaderboard`.

## Local dependencies

- .NET 10 SDK
- SQL Server, using a separate `LiveLeaderboard` database
- Docker Desktop for Redis

Redis does not need to be installed natively on Windows. Start the project-managed Redis container with:

```powershell
docker compose up -d redis
```

The app will connect to Redis at `localhost:6379`.

## Planned architecture

- SQL Server stores competitions, participants, immutable score events, current totals, and projection outbox rows.
- Redis stores each live ranking as a sorted set named `leaderboard:{competitionId}`.
- A background worker writes absolute totals with `ZADD`, which makes retries safe.
- SignalR broadcasts complete leaderboard snapshots to every browser viewing the competition.

The complete design is in [docs/superpowers/specs/2026-09-24-live-leaderboard-design.md](docs/superpowers/specs/2026-09-24-live-leaderboard-design.md).
