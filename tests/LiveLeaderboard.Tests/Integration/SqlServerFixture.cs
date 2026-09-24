using LiveLeaderboard.Data.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LiveLeaderboard.Tests.Integration;

public sealed class SqlServerFixture : IAsyncLifetime
{
    public string ConnectionString =>
        Environment.GetEnvironmentVariable("LIVE_LEADERBOARD_TEST_CONNECTION")
        ?? "Server=localhost\\SQL;Database=LiveLeaderboard_Test;Trusted_Connection=True;TrustServerCertificate=True";

    public ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            // The web host registers IdentityCore's StoreOptions before building the context.
            // The fixture creates the context directly, so suppress only this test-harness
            // warning while the migration suite validates the committed schema itself.
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "sql-server";
}
