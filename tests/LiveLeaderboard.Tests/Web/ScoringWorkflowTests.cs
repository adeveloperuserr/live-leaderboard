using Bunit;
using FluentAssertions;
using LiveLeaderboard.Core.Competitions;
using LiveLeaderboard.Web.Components.Shared;

namespace LiveLeaderboard.Tests.Web;

public sealed class ScoringWorkflowTests : BunitContext
{
    [Fact]
    public void Score_entry_form_exposes_signed_points_reason_and_participant_choice()
    {
        var participants = new List<Participant>
        {
            new() { Id = Guid.NewGuid(), DisplayName = "Ada Lovelace", Handle = "ada", IsActive = true },
            new() { Id = Guid.NewGuid(), DisplayName = "Inactive", IsActive = false }
        };

        var cut = Render<ScoreEntryForm>(parameters => parameters
            .Add(component => component.Participants, participants)
            .Add(component => component.IsSubmitting, false));

        cut.Find("input[name='points']").GetAttribute("type").Should().Be("number");
        cut.Find("textarea[name='reason']").Should().NotBeNull();
        cut.Find("select[name='participant']").TextContent.Should().Contain("Ada Lovelace");
        cut.Find("select[name='participant']").TextContent.Should().NotContain("Inactive");
    }
}
