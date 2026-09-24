using System.Diagnostics;
using System.Text;
using LiveLeaderboard.Core.Abstractions;
using LiveLeaderboard.Core.Competitions;
using LiveLeaderboard.Data.Data;
using LiveLeaderboard.Data.Redis;
using LiveLeaderboard.Data.Scoring;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace LiveLeaderboard.Tests.Browser;

public sealed class PlaywrightFixture : IAsyncLifetime
{
    public string BaseUrl { get; } = "http://127.0.0.1:5187";
    public string CompetitionSlug { get; } = $"browser-smoke-{Guid.NewGuid():N}";
    public string ScorerEmail { get; } = $"scorer-{Guid.NewGuid():N}@example.test";
    public string ScorerPassword { get; } = "BrowserTest123!";
    public Guid CompetitionId { get; private set; }
    public Guid AdaId { get; private set; }
    public string ServerLog => serverLog.ToString();
    public string ServerErrors => serverErrors.ToString();

    private Process? webProcess;
    private readonly StringBuilder serverLog = new();
    private readonly StringBuilder serverErrors = new();

    public async Task InitializeAsync()
    {
        await SeedAsync();

        var repositoryRoot = Environment.GetEnvironmentVariable("LIVE_LEADERBOARD_REPO_ROOT")
            ?? Directory.GetParent(AppContext.BaseDirectory)!.Parent!.Parent!.Parent!.Parent!.Parent!.FullName;
        webProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        webProcess.StartInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        webProcess.StartInfo.ArgumentList.Add("run");
        webProcess.StartInfo.ArgumentList.Add("--no-launch-profile");
        webProcess.StartInfo.ArgumentList.Add("--no-build");
        webProcess.StartInfo.ArgumentList.Add("--project");
        webProcess.StartInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "src", "LiveLeaderboard.Web", "LiveLeaderboard.Web.csproj"));
        webProcess.StartInfo.ArgumentList.Add("--urls");
        webProcess.StartInfo.ArgumentList.Add(BaseUrl);
        webProcess.Start();
        _ = DrainAsync(webProcess.StandardOutput);
        _ = DrainAsync(webProcess.StandardError);

        using var client = new HttpClient();
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (webProcess.HasExited)
            {
                throw new InvalidOperationException("The browser smoke-test web host exited early.");
            }

            try
            {
                using var health = await client.GetAsync(BaseUrl);
                if (health.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // The Kestrel process is still starting.
            }

            await Task.Delay(250);
        }

        throw new InvalidOperationException("The browser smoke-test web host did not start.");
    }

    private async Task DrainAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            lock (serverLog)
            {
                serverLog.AppendLine(line);
                if (serverLog.Length > 30_000)
                {
                    serverLog.Remove(0, serverLog.Length - 30_000);
                }

                if (line.Contains("fail", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("exception", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("critical", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("error", StringComparison.OrdinalIgnoreCase))
                {
                    serverErrors.AppendLine(line);
                }
            }
        }
    }

    private async Task SeedAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("LIVE_LEADERBOARD_CONNECTION")
            ?? "Server=localhost\\SQL;Database=LiveLeaderboard;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        using var db = new ApplicationDbContext(options);

        var scorer = new ApplicationUser
        {
            UserName = ScorerEmail,
            NormalizedUserName = ScorerEmail.ToUpperInvariant(),
            Email = ScorerEmail,
            NormalizedEmail = ScorerEmail.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            EmailConfirmed = true
        };
        scorer.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(scorer, ScorerPassword);
        var scorerRole = await db.Roles.SingleOrDefaultAsync(role => role.Name == "Scorer");
        if (scorerRole is null)
        {
            scorerRole = new IdentityRole { Name = "Scorer", NormalizedName = "SCORER" };
            db.Roles.Add(scorerRole);
        }
        db.Users.Add(scorer);
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = scorer.Id, RoleId = scorerRole.Id });

        var competition = new Competition
        {
            Slug = CompetitionSlug,
            Name = "Browser Smoke Challenge",
            Description = "A real browser verification competition.",
            Status = CompetitionStatus.Live
        };
        var ada = new Participant { CompetitionId = competition.Id, DisplayName = "Ada Lovelace", Handle = "ada", SortOrder = 1 };
        var grace = new Participant { CompetitionId = competition.Id, DisplayName = "Grace Hopper", Handle = "grace", SortOrder = 2 };
        competition.Participants.Add(ada);
        competition.Participants.Add(grace);
        db.Competitions.Add(competition);
        await db.SaveChangesAsync();

        CompetitionId = competition.Id;
        AdaId = ada.Id;
        var scoring = new ScoreSubmissionService(db);
        await scoring.SubmitScoreAsync(CompetitionId, ada.Id, 10, "Initial score", Guid.NewGuid().ToString("N"), scorer.Id);
        await scoring.SubmitScoreAsync(CompetitionId, grace.Id, 20, "Initial score", Guid.NewGuid().ToString("N"), scorer.Id);
        using var redis = ConnectionMultiplexer.Connect("127.0.0.1:6379,abortConnect=false");
        var store = new RedisLeaderboardStore(redis);
        await new RedisProjectionRebuilder(db, store).RebuildAsync(CompetitionId);
    }

    public async Task DisposeAsync()
    {
        if (webProcess is not null)
        {
            try
            {
                if (!webProcess.HasExited)
                {
                    webProcess.Kill(entireProcessTree: true);
                    await webProcess.WaitForExitAsync();
                }
            }
            finally
            {
                webProcess.Dispose();
            }
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class BrowserFactAttribute : FactAttribute
{
    public BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LIVE_LEADERBOARD_RUN_BROWSER_TEST") != "1")
        {
            Skip = "Set LIVE_LEADERBOARD_RUN_BROWSER_TEST=1 to run the real Chromium smoke test.";
        }
    }
}
