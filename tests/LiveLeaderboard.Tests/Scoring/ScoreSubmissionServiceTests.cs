using FluentAssertions;
using LiveLeaderboard.Core.Competitions;
using LiveLeaderboard.Data.Scoring;

namespace LiveLeaderboard.Tests.Scoring;

public sealed class ScoreSubmissionServiceTests
{
    [Fact]
    public void Validate_rejects_score_for_non_live_competitions()
    {
        var result = ScoreSubmissionValidator.Validate(
            CompetitionStatus.Draft,
            participantIsActive: true,
            points: 10,
            reason: "Round one",
            idempotencyKey: "request-1");

        result.Should().NotBeNull();
        result!.ErrorCode.Should().Be(ScoreSubmissionErrors.CompetitionNotLive);
    }

    [Fact]
    public void Validate_rejects_inactive_participants()
    {
        var result = ScoreSubmissionValidator.Validate(
            CompetitionStatus.Live,
            participantIsActive: false,
            points: 10,
            reason: "Round one",
            idempotencyKey: "request-1");

        result!.ErrorCode.Should().Be(ScoreSubmissionErrors.ParticipantInactive);
    }

    [Theory]
    [InlineData(0, "Round one")]
    [InlineData(10, "")]
    [InlineData(10, "   ")]
    public void Validate_rejects_invalid_points_or_reason(int points, string reason)
    {
        var result = ScoreSubmissionValidator.Validate(
            CompetitionStatus.Live,
            participantIsActive: true,
            points,
            reason,
            idempotencyKey: "request-1");

        result.Should().NotBeNull();
        result!.ErrorCode.Should().BeOneOf(
            ScoreSubmissionErrors.InvalidPoints,
            ScoreSubmissionErrors.InvalidReason);
    }

    [Fact]
    public void Validate_rejects_reason_longer_than_500_characters()
    {
        var result = ScoreSubmissionValidator.Validate(
            CompetitionStatus.Live,
            participantIsActive: true,
            points: 10,
            reason: new string('x', 501),
            idempotencyKey: "request-1");

        result!.ErrorCode.Should().Be(ScoreSubmissionErrors.InvalidReason);
    }
}
