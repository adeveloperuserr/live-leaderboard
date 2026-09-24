# Live Leaderboard Design

**Date:** 2026-09-24

**Status:** Approved for implementation

## Goal

Build a real, reusable competition leaderboard where authenticated scorers submit points, every public viewer sees the ranking reorder in near real time, and Redis sorted sets are used for the live ranking projection rather than SQL Server being used as the leaderboard query engine.

The application will be a standalone project at `C:\Users\josh3\Projects\live-leaderboard`. It will not modify or share runtime state with The House reservation system.

## Product scope

The first release supports multiple competitions. Each competition has participants, a public leaderboard URL, and a private scoring area.

Public users can:

- Open a competition leaderboard without signing in.
- See the current podium, full ranking, scores, rank changes, and last-updated time.
- Click a participant to view their public profile and score history summary.
- Keep the page open while scores are submitted and see rows move without refreshing.

Authenticated users with the `Scorer` role can:

- Select a competition and participant.
- Submit a signed integer point adjustment with a reason.
- See a confirmation containing the durable event id and resulting total.
- Review recent score events entered by the scoring team.

Authenticated users with the `Admin` role can additionally:

- Create and edit competitions.
- Add, rename, deactivate, and reorder participants.
- Manage scorer accounts.
- Rebuild a competition's Redis projection from the durable SQL totals.
- Inspect pending and failed projection work.

The first release does not include payments, social login, anonymous score entry, a mobile app, arbitrary scoring formulas, or a general-purpose analytics warehouse.

## Technology choices

### Web application

Use .NET 10 with ASP.NET Core and Blazor Web App using interactive server rendering. This matches the existing .NET experience in The House while keeping this project independent. ASP.NET Core Identity provides authentication and role authorization. The public pages remain accessible without an account.

### Durable storage

Use SQL Server for data that must survive Redis loss or application restarts:

- Competition and participant metadata.
- Identity users and roles.
- Immutable score events.
- Current participant totals.
- An outbox of ranking projection work.

SQL Server is not the public ranking query path. It is the system of record and audit trail.

### Redis

Use Redis through `StackExchange.Redis`. Each competition has a sorted set named `leaderboard:{competitionId}`. The sorted-set member is the participant id and the score is the participant's current total.

The key Redis operations are:

- `ZADD leaderboard:{competitionId} totalScore participantId` to write an absolute total.
- `ZREVRANGE leaderboard:{competitionId} 0 49 WITHSCORES` to read the first page in descending score order.
- `ZCARD leaderboard:{competitionId}` to show the number of ranked participants.
- `ZSCORE leaderboard:{competitionId} participantId` to read an individual score.
- `ZREM leaderboard:{competitionId} participantId` when a participant is deactivated or removed from the ranking.

The worker uses `ZADD` with an absolute total rather than `ZINCRBY`. This makes retries idempotent: replaying the same outbox message cannot double-count points.

Redis is a projection for fast ordered reads, not the durable audit log. If Redis is unavailable, score submissions remain durable in SQL and are retried by the projection worker. An administrator can rebuild a whole competition from SQL totals.

### SignalR

Use a `LeaderboardHub` at `/hubs/leaderboard`. Clients join a competition-specific group such as `competition:{competitionId}`. After the worker successfully writes a new Redis total, it reads the affected ranking slice and broadcasts a `leaderboardUpdated` message to that group.

The SignalR message contains a complete snapshot for the visible leaderboard page rather than only a single row mutation. This keeps clients simple and makes reconnect recovery safe. A disconnected client fetches the normal HTTP snapshot, then joins the SignalR group again.

The first deployment runs as one web process. The design leaves room for a Redis SignalR backplane if the application is later scaled to multiple web processes; that is not required for the local build.

## Domain model

### Competition

- `Id` — `Guid` primary key.
- `Slug` — unique public identifier used in URLs.
- `Name` — display name.
- `Description` — optional public description.
- `Status` — `Draft`, `Live`, or `Completed`.
- `CreatedAtUtc`, `UpdatedAtUtc`.

Only `Live` and `Completed` competitions are public. A competition becomes `Live` before scoring starts and can be marked `Completed` to freeze new score entry while keeping the final leaderboard visible.

### Participant

- `Id` — `Guid` primary key.
- `CompetitionId` — foreign key.
- `DisplayName` — public name.
- `Handle` — optional short public identifier.
- `SortOrder` — stable display order for equal scores and administration.
- `IsActive`.

The database enforces uniqueness of `DisplayName` within a competition. Deactivated participants remain in SQL history but are removed from the active Redis ranking.

### Score event

- `Id` — `Guid` event id.
- `CompetitionId` and `ParticipantId`.
- `Points` — signed integer adjustment; positive adds points and negative corrects a previous mistake.
- `Reason` — required human-readable explanation.
- `IdempotencyKey` — unique per competition and scorer request.
- `SubmittedByUserId`.
- `CreatedAtUtc`.

Score events are append-only. Corrections are new negative or positive events and never edits to history.

### Participant total

- `CompetitionId` and `ParticipantId` — composite primary key.
- `TotalScore` — integer total maintained transactionally with score-event insertion.
- `UpdatedAtUtc`.

This table is the efficient SQL source for Redis rebuilds. Redis remains the ranking source for public ordered reads.

### Projection outbox

- `Id` — `Guid`.
- `ScoreEventId` — unique reference to the originating score event.
- `CompetitionId`, `ParticipantId`.
- `TotalScore` — the absolute SQL total to write to Redis.
- `Status` — `Pending`, `Processing`, `Published`, or `Failed`.
- `Attempts`, `AvailableAtUtc`, `LeaseUntilUtc`, `LastError`.
- `CreatedAtUtc`, `PublishedAtUtc`.

The score event, participant total update, and outbox row are committed in one SQL transaction. The worker may retry a message safely because the payload is an absolute total and the score event id is unique.

## Data flow

### Score submission

1. The scorer submits participant, signed points, reason, and idempotency key through the authenticated application service.
2. The service verifies the competition is `Live`, the participant is active, and the scorer is authorized.
3. Inside one SQL transaction, the service inserts the score event, increments `ParticipantTotal.TotalScore`, and inserts a pending projection outbox row.
4. The service returns the durable event id and resulting total. The UI shows that the score is accepted even if Redis is temporarily unavailable.
5. The projection worker claims pending outbox rows, writes `ZADD` to Redis, reads the affected leaderboard snapshot, and broadcasts the snapshot through the competition SignalR group.
6. The worker marks the outbox row `Published`. Failed attempts use bounded exponential backoff and remain visible to administrators.

### Public read

1. The initial page request loads competition metadata and the first ranking page from Redis.
2. If the Redis key does not exist, the application reports the projection as unavailable rather than silently querying SQL for the ranking. An admin can rebuild it; the page includes a clear unavailable state.
3. The browser connects to SignalR, joins the competition group, and replaces the visible snapshot whenever `leaderboardUpdated` arrives.
4. On reconnect, the browser fetches a fresh HTTP snapshot before resuming live updates.

### Projection rebuild

1. An admin requests a rebuild for a competition.
2. The rebuild service reads active participants and their `ParticipantTotal` rows from SQL.
3. It replaces the Redis sorted set using a temporary key, then atomically renames the temporary key into place.
4. It broadcasts a fresh snapshot to connected viewers and records the rebuild result.

The temporary-key swap prevents viewers from observing a half-built ranking.

## Ranking and ties

Redis returns members by descending score. The application assigns competition ranks while mapping the snapshot:

- A higher score receives a better rank.
- Equal scores receive the same rank.
- The next distinct score uses competition ranking, so scores `100, 100, 90` display as ranks `1, 1, 3`.
- `SortOrder` is used only as a stable visual order among equal scores; it does not change the displayed rank.

The API returns both `Rank` and `Score`, so clients do not need to infer tie behavior.

## API and realtime contract

The application service owns validation and domain behavior. HTTP endpoints and Blazor components call the service rather than writing directly to Redis or EF Core.

Required application operations:

- `GetPublicLeaderboardAsync(slug, page, pageSize, cancellationToken)`.
- `SubmitScoreAsync(competitionId, participantId, points, reason, idempotencyKey, userId, cancellationToken)`.
- `GetRecentScoreEventsAsync(competitionId, limit, cancellationToken)`.
- `RebuildProjectionAsync(competitionId, cancellationToken)`.

The hub exposes only group membership. Clients cannot submit scores through SignalR. A client may call `JoinCompetition(Guid competitionId)` after the server verifies the competition is public.

The public snapshot includes:

- Competition slug, name, and status.
- Last projection timestamp.
- Total participant count.
- Ordered rows with participant id, display name, score, rank, and rank delta when known.
- A `hasMore` flag for pagination.

## Failure handling and security

- Duplicate idempotency keys return the original score event result instead of creating another event.
- A score submission is never reported as lost because Redis is down; the SQL transaction is the acceptance boundary.
- Worker retries are safe because Redis receives absolute totals.
- A stale or expired outbox lease can be reclaimed by a later worker pass.
- Redis connection failures are logged with competition and outbox identifiers but do not expose connection strings or credentials.
- Public endpoints expose only active public competition data and do not reveal scorer identity or private audit fields.
- Score entry and rebuild endpoints require role authorization and antiforgery protection.
- The application uses configuration or user secrets for SQL Server and Redis connection strings; no secrets are committed.
- Input limits include a bounded reason length, bounded page size, and validation preventing scores outside the supported integer range.

## Local development and learning path

Redis does not need to be installed natively on Windows. Docker Desktop is already available locally, so the preferred setup is a project `docker-compose.yml` that runs a Redis container with a named volume:

```powershell
docker compose up -d redis
```

The app connects to `localhost:6379` by default. The README will explain both the Docker path and the meaning of the Redis commands used by the application.

SQL Server can use the existing local SQL Server instance with a separate database named `LiveLeaderboard`. The new project has its own connection string and migrations; it does not use The House database.

The README will include a short end-to-end learning walkthrough:

1. Start Redis and SQL Server.
2. Run migrations and start the web app.
3. Create or seed a competition and participants.
4. Open the public leaderboard in two browser windows.
5. Enter points from the scorer panel and watch both windows reorder.
6. Inspect the sorted set with `redis-cli` or Redis Insight.
7. Stop Redis, submit a score, restart Redis, and observe outbox retry/rebuild behavior.

## Testing strategy

The test suite will include:

- Domain/service tests for competition state validation, signed adjustments, ties, rank mapping, and duplicate idempotency keys.
- SQL integration tests for transaction atomicity, total updates, unique score events, and outbox creation.
- Redis integration tests using a disposable Redis container for `ZADD`, ordered reads, rebuild swapping, and retry-safe absolute writes.
- Worker tests for lease claiming, retry backoff, failure recovery, and SignalR snapshot publication.
- HTTP/hub tests proving public read access, scorer/admin authorization, and competition group isolation.
- A browser-level smoke test covering two public clients receiving a reorder after a real score submission.

The completion check is not merely that a row appears in a database: two independently opened public pages must visibly converge on the same new order after a real scorer submission.

## Decision summary

This design deliberately separates responsibilities:

| Technology | Responsibility | Why it is here |
| --- | --- | --- |
| SQL Server | Durable records, audit history, totals, outbox | Keeps accepted scores safe and explainable |
| Redis sorted set | Fast ordered leaderboard projection | Demonstrates the data structure built for ranking |
| SignalR | Push completed projection updates to browsers | Makes the leaderboard live without polling |
| Background worker | Bridge SQL durability to Redis and broadcasts | Keeps submission reliable during Redis interruptions |
| Blazor Web App | Public and authenticated user experiences | Reuses the .NET/Blazor direction without modifying The House |

