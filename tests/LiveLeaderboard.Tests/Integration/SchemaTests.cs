using FluentAssertions;
using LiveLeaderboard.Core.Competitions;
using LiveLeaderboard.Core.Scoring;
using Microsoft.EntityFrameworkCore;

namespace LiveLeaderboard.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public sealed class SchemaTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Migration_creates_the_leaderboard_and_identity_tables()
    {
        await using var context = fixture.CreateContext();

        await using var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sys.tables WHERE name IN ('Competitions', 'Participants', 'ScoreEvents', 'ParticipantTotals', 'ProjectionOutboxes', 'AspNetUsers', 'AspNetRoles')";
        await using var reader = await command.ExecuteReaderAsync();
        var tableNames = new List<string>();
        while (await reader.ReadAsync())
        {
            tableNames.Add(reader.GetString(0));
        }

        tableNames.Should().BeEquivalentTo(
            "Competitions",
            "Participants",
            "ScoreEvents",
            "ParticipantTotals",
            "ProjectionOutboxes",
            "AspNetUsers",
            "AspNetRoles");
    }

    [Fact]
    public async Task Model_contains_unique_indexes_for_public_and_idempotency_keys()
    {
        await using var context = fixture.CreateContext();

        context.Model.FindEntityType(typeof(Competition))!
            .GetIndexes()
            .Single(index => index.Properties.Single().Name == nameof(Competition.Slug))
            .IsUnique.Should().BeTrue();

        context.Model.FindEntityType(typeof(Participant))!
            .GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(Participant.CompetitionId), nameof(Participant.DisplayName)]))
            .IsUnique.Should().BeTrue();

        context.Model.FindEntityType(typeof(ScoreEvent))!
            .GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(ScoreEvent.CompetitionId), nameof(ScoreEvent.IdempotencyKey)]))
            .IsUnique.Should().BeTrue();
    }
}
