# Live Leaderboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Build a real multi-competition leaderboard with durable SQL score events, Redis sorted-set ranking, authenticated score entry, and live SignalR updates for public viewers.

**Architecture:** SQL Server is the durable system of record for competitions, participants, score events, totals, and an outbox. A background worker projects absolute participant totals into Redis sorted sets, reads ordered snapshots, and broadcasts them through SignalR competition groups. A .NET 10 Blazor Web App provides public live pages and role-protected scorer/admin pages.

**Tech Stack:** .NET 10, ASP.NET Core, Blazor Web App with interactive server rendering, ASP.NET Core Identity, Entity Framework Core with SQL Server, StackExchange.Redis, SignalR, xUnit, Docker Compose Redis, and Playwright for the browser smoke test.

**Spec:** docs/superpowers/specs/2026-09-24-live-leaderboard-design.md

## Global Constraints

- The application is a standalone project at C:\Users\josh3\Projects\live-leaderboard and must not modify The House project or its database.
- SQL Server stores durable records, audit history, participant totals, and projection outbox work; it is not the public ordered-ranking query path.
- Redis stores each live ranking in leaderboard:{competitionId} as a sorted set whose members are participant ids and whose scores are absolute participant totals.
- Projection writes use ZADD with absolute totals so retries cannot double-count points.
- Public pages read ranking snapshots from Redis and receive replacement snapshots through SignalR; they do not poll SQL for ranking data.
- Score events are append-only signed integer adjustments; corrections are new events rather than edits.
- Duplicate score submissions are prevented by a unique idempotency key per competition.
- Only Live and Completed competitions are public; only Live competitions accept score submissions.
- Scoring and projection work must remain safe when Redis is unavailable: SQL acceptance succeeds, outbox work retries, and the UI communicates projection delay.
- No secrets, passwords, or production connection strings may be committed.
- Redis is run locally through Docker Compose with a named volume and the default connection localhost:6379.

## Review Focus

- Duplicate score requests must return the original result without changing the total twice; pinned by the score-service idempotency test in Task 4.
- A committed score must survive Redis being offline and project after Redis returns; pinned by the outbox/worker retry test in Task 6.
- Equal scores must receive equal competition ranks while rows retain deterministic visual order; pinned by the rank-mapping tests in Task 5.
- A viewer must never receive another competition's updates; pinned by the hub group-isolation test in Task 7.
- Two real browser pages must converge after one score submission without a manual refresh; pinned by the Playwright smoke test in Task 10.

### Task 1: Create the isolated solution and local infrastructure

**Files:**
- Create: LiveLeaderboard.slnx
- Create: src/LiveLeaderboard.Core/LiveLeaderboard.Core.csproj
- Create: src/LiveLeaderboard.Data/LiveLeaderboard.Data.csproj
- Create: src/LiveLeaderboard.Web/LiveLeaderboard.Web.csproj
- Create: tests/LiveLeaderboard.Tests/LiveLeaderboard.Tests.csproj
- Create: docker-compose.yml
- Create: .gitignore
- Create: README.md
- Test: restore/build commands for the solution

**Interfaces:**
- Produces the project references used by every later task and a Redis service reachable at localhost:6379.

- [ ] **Step 1: Create project files and references**

Run:

~~~powershell
New-Item -ItemType Directory -Force src, tests | Out-Null
dotnet new sln -n LiveLeaderboard
dotnet new classlib -n LiveLeaderboard.Core -o src/LiveLeaderboard.Core --framework net10.0
dotnet new classlib -n LiveLeaderboard.Data -o src/LiveLeaderboard.Data --framework net10.0
dotnet new blazor -n LiveLeaderboard.Web -o src/LiveLeaderboard.Web --framework net10.0 --interactivity Server --auth Individual
dotnet new xunit -n LiveLeaderboard.Tests -o tests/LiveLeaderboard.Tests --framework net10.0
dotnet sln LiveLeaderboard.slnx add src/LiveLeaderboard.Core/LiveLeaderboard.Core.csproj src/LiveLeaderboard.Data/LiveLeaderboard.Data.csproj src/LiveLeaderboard.Web/LiveLeaderboard.Web.csproj tests/LiveLeaderboard.Tests/LiveLeaderboard.Tests.csproj
dotnet add src/LiveLeaderboard.Data/LiveLeaderboard.Data.csproj reference src/LiveLeaderboard.Core/LiveLeaderboard.Core.csproj
dotnet add src/LiveLeaderboard.Web/LiveLeaderboard.Web.csproj reference src/LiveLeaderboard.Core/LiveLeaderboard.Core.csproj src/LiveLeaderboard.Data/LiveLeaderboard.Data.csproj
dotnet add tests/LiveLeaderboard.Tests/LiveLeaderboard.Tests.csproj reference src/LiveLeaderboard.Core/LiveLeaderboard.Core.csproj src/LiveLeaderboard.Data/LiveLeaderboard.Data.csproj src/LiveLeaderboard.Web/LiveLeaderboard.Web.csproj
~~~

Expected: all four projects appear in dotnet sln list and the solution has no reference back to The House.

- [ ] **Step 2: Add the runtime packages**

Add EF Core SQL Server/design packages, StackExchange.Redis, ASP.NET Identity EF stores, and Microsoft.AspNetCore.SignalR.Client to the projects that use them. Add xUnit, FluentAssertions, and Microsoft.AspNetCore.Mvc.Testing to the test project. Keep package versions aligned with the installed .NET 10 SDK and record resolved versions in the project files.

Expected: dotnet restore LiveLeaderboard.slnx succeeds without package-version drift between projects.

- [ ] **Step 3: Add Docker Compose Redis and safe local configuration**

Create docker-compose.yml with a Redis 7 image, named live-leaderboard-redis-data volume, port 6379:6379, and a healthcheck using redis-cli ping. Add appsettings.json keys for ConnectionStrings:DefaultConnection and ConnectionStrings:Redis, with local development defaults targeting the separate LiveLeaderboard SQL Server database and localhost:6379.

Expected: docker compose up -d redis starts a healthy container and docker compose down stops it without deleting the named volume.

- [ ] **Step 4: Establish the baseline build and commit**

Run:

~~~powershell
dotnet restore LiveLeaderboard.slnx
dotnet build LiveLeaderboard.slnx --no-restore
dotnet test tests/LiveLeaderboard.Tests/LiveLeaderboard.Tests.csproj --no-restore
~~~

Expected: build and empty test project pass. Commit with git add . followed by git commit -m "chore: scaffold live leaderboard solution".

### Task 2: Define the core domain and ranking contracts

**Files:**
- Create: src/LiveLeaderboard.Core/Competitions/CompetitionStatus.cs
- Create: src/LiveLeaderboard.Core/Competitions/Competition.cs
- Create: src/LiveLeaderboard.Core/Competitions/Participant.cs
- Create: src/LiveLeaderboard.Core/Scoring/ScoreEvent.cs
- Create: src/LiveLeaderboard.Core/Scoring/ParticipantTotal.cs
- Create: src/LiveLeaderboard.Core/Scoring/ProjectionOutbox.cs
- Create: src/LiveLeaderboard.Core/Scoring/ProjectionStatus.cs
- Create: src/LiveLeaderboard.Core/Leaderboards/LeaderboardSnapshot.cs
- Create: src/LiveLeaderboard.Core/Leaderboards/LeaderboardRow.cs
- Create: src/LiveLeaderboard.Core/Scoring/ScoreSubmission.cs
- Create: src/LiveLeaderboard.Core/Abstractions/ILeaderboardReader.cs
- Create: src/LiveLeaderboard.Core/Abstractions/ILeaderboardProjector.cs
- Create: src/LiveLeaderboard.Core/Abstractions/IScoreSubmissionService.cs
- Create: src/LiveLeaderboard.Core/Abstractions/ICompetitionCatalog.cs
- Test: tests/LiveLeaderboard.Tests/Core/LeaderboardSnapshotTests.cs

**Interfaces:**
- IScoreSubmissionService.SubmitScoreAsync(Guid competitionId, Guid participantId, int points, string reason, string idempotencyKey, string userId, CancellationToken cancellationToken) returns an event id, resulting total, and replay flag.
- ILeaderboardReader.GetSnapshotAsync(Guid competitionId, int page, int pageSize, CancellationToken cancellationToken) returns a LeaderboardSnapshot or an unavailable result when Redis has no projection.
- ILeaderboardProjector.ProjectAsync(ProjectionOutbox work, CancellationToken cancellationToken) writes an absolute total and returns the updated snapshot.
- LeaderboardSnapshot.Rows contains participant id, display name, score, rank, and optional rank delta; HasMore is explicit.

- [ ] **Step 1: Write failing tests for competition-rank mapping**

Cover scores 100, 100, 90, negative totals, empty snapshots, page-size limits, and deterministic SortOrder for equal scores. Assert ranks 1, 1, 3 and never derive rank from the row index alone.

- [ ] **Step 2: Run the focused test and verify it fails**

Run dotnet test tests/LiveLeaderboard.Tests/LiveLeaderboard.Tests.csproj --filter FullyQualifiedName~LeaderboardSnapshotTests.

Expected: FAIL because the domain snapshot mapper and records do not exist.

- [ ] **Step 3: Implement domain records and the pure snapshot mapper**

Use immutable records where possible. Keep domain types independent of EF Core, Redis, SignalR, and Blazor. Implement a pure LeaderboardSnapshotMapper.Map that receives ordered participant/score data and calculates competition rank, stable tie order, HasMore, and rank delta against an optional previous snapshot.

- [ ] **Step 4: Run the focused tests and full test project**

Run the focused command, then dotnet test tests/LiveLeaderboard.Tests/LiveLeaderboard.Tests.csproj.

Expected: all domain tests pass and no data-layer package is required by Core.

- [ ] **Step 5: Commit the core contracts**

Run git add src/LiveLeaderboard.Core tests/LiveLeaderboard.Tests/Core followed by git commit -m "feat: define leaderboard domain contracts".

### Task 3: Build SQL persistence, Identity, migrations, and seed data

**Files:**
- Create: src/LiveLeaderboard.Data/Data/ApplicationDbContext.cs
- Create: src/LiveLeaderboard.Data/Data/ApplicationUser.cs
- Create: src/LiveLeaderboard.Data/Data/DbInitializer.cs
- Create: src/LiveLeaderboard.Data/Data/Configurations/CompetitionConfiguration.cs
- Create: src/LiveLeaderboard.Data/Data/Configurations/ParticipantConfiguration.cs
- Create: src/LiveLeaderboard.Data/Data/Configurations/ScoreEventConfiguration.cs
- Create: src/LiveLeaderboard.Data/Data/Configurations/ParticipantTotalConfiguration.cs
- Create: src/LiveLeaderboard.Data/Data/Configurations/ProjectionOutboxConfiguration.cs
- Create: src/LiveLeaderboard.Data/Data/AppDbContextFactory.cs
- Create: src/LiveLeaderboard.Data/Migrations/*
- Modify: src/LiveLeaderboard.Web/Program.cs
- Modify: src/LiveLeaderboard.Web/appsettings.Development.json
- Test: tests/LiveLeaderboard.Tests/Integration/SqlServerFixture.cs
- Test: tests/LiveLeaderboard.Tests/Integration/SchemaTests.cs

**Interfaces:**
- ApplicationDbContext exposes DbSet for Competition, Participant, ScoreEvent, ParticipantTotal, and ProjectionOutbox.
- SQL indexes enforce unique competition slugs, participant display names per competition, and idempotency keys per competition.
- The web host registers Identity roles Admin and Scorer without seeding credentials into source control.

- [ ] **Step 1: Write failing schema tests**

Create a SQL Server fixture that only permits a database named LiveLeaderboard_Test. Add tests that migrate a clean database and verify required tables, unique indexes, signed integer columns, outbox status, and Identity tables exist.

- [ ] **Step 2: Run schema tests to verify the fixture fails**

Run dotnet test tests/LiveLeaderboard.Tests/LiveLeaderboard.Tests.csproj --filter FullyQualifiedName~SchemaTests.

Expected: FAIL because the context, migrations, and connection registration do not exist.

- [ ] **Step 3: Implement the context and entity configurations**

Configure UTC timestamps, required lengths for slug/name/reason/idempotency key, cascade behavior that preserves score history, composite keys for participant totals, and indexes for public lookups and worker polling. Configure ApplicationUser through Identity EF stores.

- [ ] **Step 4: Register SQL Server and Identity in the web host**

Add AddDbContext<ApplicationDbContext>, AddIdentityCore<ApplicationUser>(), roles, EF stores, cookie authentication, authorization policies for Scorer and Admin, and the development connection string. Do not seed a password in code.

- [ ] **Step 5: Create and apply the initial migration**

Run:

~~~powershell
dotnet ef migrations add InitialLeaderboardSchema --project src/LiveLeaderboard.Data --startup-project src/LiveLeaderboard.Web --output-dir Migrations
dotnet ef database update --project src/LiveLeaderboard.Data --startup-project src/LiveLeaderboard.Web
~~~

Expected: LiveLeaderboard is created or updated, while the existing ScreeningReservation database remains untouched.

- [ ] **Step 6: Add safe development seed data**

Seed one Draft competition, three participants, the roles, and a development admin/scorer only when explicitly enabled by a SeedDemoData configuration flag. Read development credentials from user secrets or environment variables. Make the seed idempotent.

- [ ] **Step 7: Run schema tests and commit**

Run focused schema tests and the full test project. Commit with git add . followed by git commit -m "feat: add durable leaderboard persistence".

### Task 4: Implement transactional score submission and idempotency

**Files:**
- Create: src/LiveLeaderboard.Data/Scoring/ScoreSubmissionService.cs
- Create: src/LiveLeaderboard.Data/Scoring/ScoreSubmissionErrors.cs
- Create: src/LiveLeaderboard.Data/Scoring/ScoreEventQueryService.cs
- Modify: src/LiveLeaderboard.Data/Data/ApplicationDbContext.cs
- Test: tests/LiveLeaderboard.Tests/Scoring/ScoreSubmissionServiceTests.cs
- Test: tests/LiveLeaderboard.Tests/Integration/ScoreSubmissionSqlTests.cs

**Interfaces:**
- ScoreSubmissionService implements IScoreSubmissionService and is the only component that writes score events and participant totals.
- A successful result returns ScoreEventId, ParticipantId, NewTotalScore, and WasReplayed.
- A rejected result identifies CompetitionNotLive, ParticipantInactive, InvalidReason, InvalidPoints, or IdempotencyConflict.

- [ ] **Step 1: Write failing unit tests for validation and duplicate requests**

Test draft/completed competitions reject points, inactive participants reject points, blank or overlong reasons reject points, the same idempotency key returns the original event, and a reused key with different payload fails with IdempotencyConflict.

- [ ] **Step 2: Write failing SQL integration tests for atomic updates**

Test one successful submission creates exactly one score event, increments the participant total, and creates one pending outbox row containing the absolute new total. Test a duplicate request leaves event count, total, and outbox count unchanged.

- [ ] **Step 3: Implement the transaction**

Load competition and participant with state checks. Begin a SQL transaction, check the idempotency key, insert the immutable score event, update or insert ParticipantTotal, add the outbox row with the resulting absolute total, and commit. Catch unique-key races and re-read the existing event so concurrent duplicate requests converge on one result.

- [ ] **Step 4: Implement recent event queries**

Return only public-safe fields to scorer/admin pages. Keep SubmittedByUserId server-side and authorize the query by role and competition access.

- [ ] **Step 5: Run all score tests and commit**

Run focused unit/integration tests and then the full suite. Commit with git add . followed by git commit -m "feat: add durable idempotent score submission".

### Task 5: Implement Redis sorted-set projection and snapshot reads

**Files:**
- Create: src/LiveLeaderboard.Data/Redis/RedisLeaderboardStore.cs
- Create: src/LiveLeaderboard.Data/Redis/RedisKeyFactory.cs
- Create: src/LiveLeaderboard.Data/Redis/RedisLeaderboardReader.cs
- Create: src/LiveLeaderboard.Data/Redis/RedisProjectionRebuilder.cs
- Modify: src/LiveLeaderboard.Web/Program.cs
- Test: tests/LiveLeaderboard.Tests/Redis/RedisLeaderboardStoreTests.cs
- Test: tests/LiveLeaderboard.Tests/Redis/RedisProjectionRebuilderTests.cs

**Interfaces:**
- RedisLeaderboardStore.SetAbsoluteScoreAsync(Guid competitionId, Guid participantId, int totalScore, CancellationToken) performs ZADD.
- RedisLeaderboardReader.GetSnapshotAsync(Guid competitionId, int page, int pageSize, CancellationToken) uses ZREVRANGE with scores, loads participant metadata from SQL, and maps ties/ranks through Core.
- RedisProjectionRebuilder.RebuildAsync(Guid competitionId, CancellationToken) writes to a temporary key and atomically swaps it into the canonical key.

- [ ] **Step 1: Start Redis and write failing integration tests**

Run docker compose up -d redis. Add tests for descending score order, absolute-write retry safety, ZCARD, ZREM, missing-key behavior, and temporary-key rebuild swapping. Use a unique key prefix per test and delete only those keys in fixture cleanup.

- [ ] **Step 2: Implement key generation and the sorted-set adapter**

Centralize the canonical key format. Use one ConnectionMultiplexer singleton. Keep all Redis calls behind the adapter so the rest of the application never depends on StackExchange.Redis types.

- [ ] **Step 3: Implement snapshot reads and tie-aware mapping**

Read a page of participant ids and scores from Redis, join active metadata from SQL, preserve Redis order among equal scores, compute competition ranks, and return HasMore using sorted-set cardinality. If the canonical key does not exist, return an explicit unavailable result.

- [ ] **Step 4: Implement atomic rebuild**

Read active participants and SQL totals, write all entries to leaderboard:{competitionId}:rebuild:{operationId}, remove stale inactive members by replacing rather than mutating the canonical key, and execute the Redis rename/swap atomically.

- [ ] **Step 5: Run Redis tests and commit**

Run Redis-focused tests, stop and restart the container once, and rerun them to verify the named volume is not required for correctness. Commit with git add . followed by git commit -m "feat: project rankings into Redis sorted sets".

### Task 6: Build the outbox worker and retry behavior

**Files:**
- Create: src/LiveLeaderboard.Data/Scoring/ProjectionOutboxStore.cs
- Create: src/LiveLeaderboard.Web/Workers/LeaderboardProjectionWorker.cs
- Create: src/LiveLeaderboard.Web/Workers/ProjectionRetryPolicy.cs
- Create: src/LiveLeaderboard.Core/Abstractions/IProjectionNotifier.cs
- Modify: src/LiveLeaderboard.Web/Program.cs
- Test: tests/LiveLeaderboard.Tests/Workers/ProjectionRetryPolicyTests.cs
- Test: tests/LiveLeaderboard.Tests/Workers/LeaderboardProjectionWorkerTests.cs

**Interfaces:**
- ProjectionOutboxStore.ClaimBatchAsync(DateTime utcNow, int batchSize, TimeSpan lease, CancellationToken) claims pending or expired-processing rows.
- IProjectionNotifier.PublishAsync(LeaderboardSnapshot snapshot, CancellationToken) is implemented by the SignalR adapter in Task 7 and a test spy in worker tests.
- LeaderboardProjectionWorker claims work, calls the Redis projector, publishes a complete snapshot, and marks work published only after both succeed.

- [ ] **Step 1: Write failing retry and lease tests**

Test pending rows are claimed once, expired leases are reclaimable, Redis failures increase attempts and schedule bounded backoff, successful projection writes PublishedAtUtc, and a notifier failure leaves work retryable.

- [ ] **Step 2: Implement SQL outbox claiming**

Use a short SQL transaction and row locks to claim a bounded batch. Set Processing, increment attempts, and assign LeaseUntilUtc. Do not hold a SQL transaction while calling Redis.

- [ ] **Step 3: Implement retry policy**

Use delays of 1, 2, 4, 8, 16, and 30 seconds capped at 30 seconds. After the configured maximum attempt count, mark Failed with a redacted error message and leave the row visible to admins.

- [ ] **Step 4: Implement the hosted worker**

Use a cancellation-aware loop with a short idle delay, a longer failure delay, and a clean shutdown path. For each work item, call absolute Redis projection, build the current snapshot, notify subscribers, then mark published.

- [ ] **Step 5: Test Redis outage recovery and commit**

Stop Redis, submit a real SQL-backed score, verify the outbox remains pending/processing, restart Redis, and verify the worker publishes the absolute total exactly once. Commit with git add . followed by git commit -m "feat: add reliable leaderboard projection worker".

### Task 7: Add SignalR realtime delivery and client subscription

**Files:**
- Create: src/LiveLeaderboard.Web/Realtime/LeaderboardHub.cs
- Create: src/LiveLeaderboard.Web/Realtime/LeaderboardNotifier.cs
- Create: src/LiveLeaderboard.Web/Realtime/LeaderboardGroupNames.cs
- Create: src/LiveLeaderboard.Web/Components/Leaderboard/LeaderboardRealtimeClient.cs
- Modify: src/LiveLeaderboard.Web/Program.cs
- Test: tests/LiveLeaderboard.Tests/Realtime/LeaderboardHubTests.cs
- Test: tests/LiveLeaderboard.Tests/Realtime/LeaderboardNotifierTests.cs

**Interfaces:**
- LeaderboardHub.JoinCompetition(Guid competitionId) validates the competition is public before joining competition:{competitionId}.
- LeaderboardNotifier implements IProjectionNotifier by calling IHubContext<LeaderboardHub>.Clients.Group(group).SendAsync("leaderboardUpdated", snapshot).
- LeaderboardRealtimeClient owns a per-page HubConnection, invokes group join, handles leaderboardUpdated, reconnects, and requests a fresh HTTP snapshot after reconnect.

- [ ] **Step 1: Write failing hub isolation tests**

Test a public competition can be joined, a Draft competition cannot, and a connection joined to competition A does not receive notifications sent to competition B.

- [ ] **Step 2: Implement hub and group naming**

Keep the hub limited to public group membership. Do not expose score submission or admin operations over SignalR.

- [ ] **Step 3: Implement the notifier**

Send complete snapshots to the competition group after Redis projection succeeds. Include projection timestamp and rows in every message.

- [ ] **Step 4: Implement the Blazor realtime client**

Build the connection with the current app origin and /hubs/leaderboard, subscribe to leaderboardUpdated, enable automatic reconnect, and expose events to the page component. On reconnect, invoke the page's HTTP reload callback before marking the client live.

- [ ] **Step 5: Run hub/worker tests and commit**

Run focused realtime tests and commit with git add . followed by git commit -m "feat: broadcast live leaderboard snapshots".

### Task 8: Build public leaderboard and participant experiences

**Files:**
- Create: src/LiveLeaderboard.Web/Components/Pages/Leaderboard.razor
- Create: src/LiveLeaderboard.Web/Components/Pages/Participant.razor
- Create: src/LiveLeaderboard.Web/Components/Leaderboard/LeaderboardTable.razor
- Create: src/LiveLeaderboard.Web/Components/Leaderboard/Podium.razor
- Create: src/LiveLeaderboard.Web/Components/Leaderboard/ProjectionStatus.razor
- Modify: src/LiveLeaderboard.Web/Components/Pages/Home.razor
- Modify: src/LiveLeaderboard.Web/Components/Layout/NavMenu.razor
- Modify: src/LiveLeaderboard.Web/wwwroot/app.css
- Test: tests/LiveLeaderboard.Tests/Web/PublicLeaderboardTests.cs

**Interfaces:**
- Route /leaderboards/{slug} renders a public live leaderboard.
- Route /leaderboards/{slug}/participants/{participantId:guid} renders the public participant view.
- LeaderboardTable accepts a LeaderboardSnapshot and callback for pagination/reload; it never queries EF Core or Redis directly.

- [ ] **Step 1: Write failing component tests for public states**

Cover loading, live data, completed competition, Redis projection unavailable, empty competition, equal-score ranks, and a visible last-updated indicator.

- [ ] **Step 2: Implement the public route and initial snapshot load**

Resolve the competition by slug, request the Redis snapshot through the application service, and render explicit unavailable/error states without falling back to SQL ranking queries.

- [ ] **Step 3: Implement the podium and table**

Show the top three when present, rank, participant name/handle, score, rank delta, and stable equal-score order. Add a Live status indicator and a load-more action for pages after the first 50.

- [ ] **Step 4: Attach the realtime client**

Join the competition group after initial render, replace the component snapshot on leaderboardUpdated, animate only row movement/status changes, and dispose the hub connection when the component is removed.

- [ ] **Step 5: Add participant details and responsive styling**

Use public participant id and competition slug, show current score, rank, and a non-sensitive score-event summary, and make the table readable on narrow screens. Do not expose scorer identity.

- [ ] **Step 6: Run public page tests and commit**

Run component tests and dotnet build. Commit with git add . followed by git commit -m "feat: add public live leaderboard pages".

### Task 9: Build scorer/admin workflows and authorization

**Files:**
- Create: src/LiveLeaderboard.Web/Components/Pages/Scoring.razor
- Create: src/LiveLeaderboard.Web/Components/Pages/Admin/Competitions.razor
- Create: src/LiveLeaderboard.Web/Components/Pages/Admin/Participants.razor
- Create: src/LiveLeaderboard.Web/Components/Pages/Admin/ProjectionHealth.razor
- Create: src/LiveLeaderboard.Web/Components/Shared/ScoreEntryForm.razor
- Create: src/LiveLeaderboard.Web/Authorization/LeaderboardPolicies.cs
- Modify: Identity pages generated by the Blazor template for role-aware navigation
- Modify: src/LiveLeaderboard.Web/Components/Layout/NavMenu.razor
- Test: tests/LiveLeaderboard.Tests/Web/AuthorizationTests.cs
- Test: tests/LiveLeaderboard.Tests/Web/ScoringWorkflowTests.cs

**Interfaces:**
- /scoring requires Scorer and submits only through IScoreSubmissionService.
- /admin/competitions, /admin/participants, and /admin/projection-health require Admin.
- Admin rebuild actions call RedisProjectionRebuilder through an authorized application service and never accept a raw Redis key from the browser.

- [ ] **Step 1: Write failing authorization tests**

Test anonymous/public access to leaderboard pages, scorer access to score entry, denial of scorer access to admin pages, admin access to all private pages, and competition-level checks in the hub.

- [ ] **Step 2: Implement role-aware navigation and route guards**

Use Authorize(Roles = "Scorer,Admin") for scoring and Authorize(Roles = "Admin") for administration. Hide navigation links for users without the role while preserving server-side authorization.

- [ ] **Step 3: Implement score entry**

Validate signed integer points, required reason, active participant, and live competition. Generate an idempotency key per form submission and show the durable event id, resulting total, and projection status returned by the service.

- [ ] **Step 4: Implement competition and participant administration**

Support create/edit status transitions, participant add/edit/deactivate/reorder, and safe slug validation. Prevent deleting history-bearing participants; deactivate them and remove them from the projection instead.

- [ ] **Step 5: Implement projection health and rebuild**

Show pending/processing/failed outbox counts, last error, last attempt, Redis availability, and a rebuild button. Require confirmation for rebuild and show completion status.

- [ ] **Step 6: Run authorization/scoring tests and commit**

Run focused web tests, then the full suite. Commit with git add . followed by git commit -m "feat: add scoring and admin workflows".

### Task 10: Finish documentation, operational verification, and browser smoke coverage

**Files:**
- Modify: README.md
- Create: docs/architecture/redis-signalr-data-flow.md
- Create: tests/LiveLeaderboard.Tests/Browser/LiveLeaderboardPlaywrightTests.cs
- Create: tests/LiveLeaderboard.Tests/Browser/PlaywrightFixture.cs
- Modify: src/LiveLeaderboard.Web/appsettings.Development.json
- Modify: .gitignore

**Interfaces:**
- README commands start Redis, apply migrations, run the app, create/use development users, inspect sorted-set keys, simulate Redis interruption, and run tests.
- Browser test starts the real web app and real Redis, opens two public pages, submits through the real scoring UI, and observes the same reordered leaderboard in both windows.

- [ ] **Step 1: Document the learning path**

Explain in plain language what SQL Server, Redis sorted sets, SignalR, the outbox, and the worker do. Include:

~~~powershell
docker compose up -d redis
dotnet ef database update --project src/LiveLeaderboard.Data --startup-project src/LiveLeaderboard.Web
dotnet run --project src/LiveLeaderboard.Web
~~~

Include redis-cli ZREVRANGE leaderboard:<competition-id> 0 -1 WITHSCORES, how to stop/start Redis safely, and what the user should observe after recovery.

- [ ] **Step 2: Add the data-flow diagram**

Document the path scorer -> SQL transaction -> outbox -> worker -> Redis ZADD -> SignalR group -> browsers, plus the Redis outage and rebuild paths.

- [ ] **Step 3: Write the browser smoke test**

Seed a live competition with two participants, open two public pages, submit points from a scorer context, wait for leaderboardUpdated, and assert both pages show the same new order without navigation or reload.

- [ ] **Step 4: Run the complete verification matrix**

Run:

~~~powershell
docker compose up -d redis
dotnet restore LiveLeaderboard.slnx
dotnet test LiveLeaderboard.slnx
dotnet build LiveLeaderboard.slnx --no-restore
~~~

Then run the browser smoke test, inspect Redis with redis-cli, stop Redis, submit a score, restart Redis, and confirm the outbox worker catches up.

- [ ] **Step 5: Review security and repository hygiene**

Confirm no connection strings contain passwords, no development credentials are committed, .gitignore excludes local secrets and test artifacts, all public routes hide private audit fields, and the working tree contains only intentional files.

- [ ] **Step 6: Commit the verified release**

Run git add . followed by git commit -m "docs: document live leaderboard operations and verification" only after the full matrix passes.

## Plan Self-Review

- Spec coverage: product scope is covered by Tasks 3, 4, 8, and 9; Redis projection and rebuild are covered by Tasks 5 and 6; SignalR delivery is covered by Task 7; failure handling and idempotency are covered by Tasks 4–6; local learning is covered by Tasks 1 and 10; testing requirements are covered throughout and finalized in Task 10.
- Placeholder scan: every implementation step names its files, behavior, test command, or expected result; no unfinished instructions remain.
- Type consistency: all later tasks consume LeaderboardSnapshot, IScoreSubmissionService, ILeaderboardReader, ILeaderboardProjector, and IProjectionNotifier names established in earlier tasks.
- Review focus coverage: duplicate submissions are tested in Task 4, Redis outage recovery in Task 6, ties in Tasks 2 and 5, hub isolation in Task 7, and two-browser convergence in Task 10.
- Scope check: the plan remains one application with independent persistence, projection, realtime, public UI, and private UI tasks; it does not introduce unrelated billing, analytics, or mobile work.
