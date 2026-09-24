using LiveLeaderboard.Data.Data;
using Microsoft.EntityFrameworkCore;

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
