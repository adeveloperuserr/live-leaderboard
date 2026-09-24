using FluentAssertions;
using LiveLeaderboard.Core.Competitions;
using LiveLeaderboard.Core.Scoring;
using LiveLeaderboard.Data.Data;
using LiveLeaderboard.Data.Scoring;
using Microsoft.EntityFrameworkCore;

namespace LiveLeaderboard.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public sealed class ScoreSubmissionSqlTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Submit_score_updates_total_and_creates_one_pending_projection()
    {
        var (competitionId, participantId) = await CreateLiveCompetitionAsync();
        await using var context = fixture.CreateContext();
        var service = new ScoreSubmissionService(context);

        var result = await service.SubmitScoreAsync(
            competitionId,
            participantId,
            points: 25,
            reason: "Round one",
            idempotencyKey: "submission-1",
            userId: "scorer-1");

        result.Succeeded.Should().BeTrue();
        result.NewTotalScore.Should().Be(25);
        (await context.ScoreEvents.CountAsync(scoreEvent => scoreEvent.CompetitionId == competitionId)).Should().Be(1);
        (await context.ParticipantTotals.SingleAsync(total => total.ParticipantId == participantId)).TotalScore.Should().Be(25);
        var outbox = await context.ProjectionOutboxes.SingleAsync(work => work.ParticipantId == participantId);
        outbox.Status.Should().Be(ProjectionStatus.Pending);
        outbox.TotalScore.Should().Be(25);
    }

    [Fact]
    public async Task Replaying_the_same_submission_does_not_double_count_or_create_another_outbox_row()
    {
        var (competitionId, participantId) = await CreateLiveCompetitionAsync();
        await using var context = fixture.CreateContext();
        var service = new ScoreSubmissionService(context);

        var first = await service.SubmitScoreAsync(competitionId, participantId, 10, "Round one", "submission-2", "scorer-1");
        var replay = await service.SubmitScoreAsync(competitionId, participantId, 10, "Round one", "submission-2", "scorer-1");

        first.Succeeded.Should().BeTrue();
        replay.Succeeded.Should().BeTrue();
        replay.WasReplayed.Should().BeTrue();
        replay.ScoreEventId.Should().Be(first.ScoreEventId);
        (await context.ScoreEvents.CountAsync(scoreEvent => scoreEvent.CompetitionId == competitionId)).Should().Be(1);
        (await context.ParticipantTotals.SingleAsync(total => total.ParticipantId == participantId)).TotalScore.Should().Be(10);
        (await context.ProjectionOutboxes.CountAsync(work => work.ParticipantId == participantId)).Should().Be(1);
    }

    [Fact]
    public async Task Reusing_an_idempotency_key_for_a_different_payload_is_rejected()
    {
        var (competitionId, participantId) = await CreateLiveCompetitionAsync();
        await using var context = fixture.CreateContext();
        var service = new ScoreSubmissionService(context);

        _ = await service.SubmitScoreAsync(competitionId, participantId, 10, "Round one", "submission-3", "scorer-1");
        var conflict = await service.SubmitScoreAsync(competitionId, participantId, 20, "Round two", "submission-3", "scorer-1");

        conflict.Succeeded.Should().BeFalse();
        conflict.ErrorCode.Should().Be(ScoreSubmissionErrors.IdempotencyConflict);
    }

    private async Task<(Guid CompetitionId, Guid ParticipantId)> CreateLiveCompetitionAsync()
    {
        await using var context = fixture.CreateContext();
        var competition = new Competition
        {
            Slug = $"live-{Guid.NewGuid():N}",
            Name = "Live test",
            Status = CompetitionStatus.Live
        };
        var participant = new Participant
        {
            CompetitionId = competition.Id,
            DisplayName = $"Participant {Guid.NewGuid():N}",
            SortOrder = 1
        };
        competition.Participants.Add(participant);
        context.Competitions.Add(competition);
        await context.SaveChangesAsync();
        return (competition.Id, participant.Id);
    }
}
