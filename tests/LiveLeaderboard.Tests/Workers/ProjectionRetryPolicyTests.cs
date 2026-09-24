using FluentAssertions;
using LiveLeaderboard.Core.Scoring;

namespace LiveLeaderboard.Tests.Workers;

public sealed class ProjectionRetryPolicyTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(12, 30)]
    public void Delay_is_bounded_exponential_backoff(int attempts, int expectedSeconds)
    {
        ProjectionRetryPolicy.GetDelay(attempts).Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void The_sixth_attempt_is_the_last_retry()
    {
        ProjectionRetryPolicy.ShouldFail(5).Should().BeFalse();
        ProjectionRetryPolicy.ShouldFail(6).Should().BeTrue();
    }
}
