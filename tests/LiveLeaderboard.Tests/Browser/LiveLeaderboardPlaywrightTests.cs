using FluentAssertions;
using Microsoft.Playwright;

namespace LiveLeaderboard.Tests.Browser;

public sealed class LiveLeaderboardPlaywrightTests(PlaywrightFixture fixture) : IClassFixture<PlaywrightFixture>
{
    [BrowserFact]
    public async Task Two_public_pages_converge_after_a_real_score_submission()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

        var firstPage = await browser.NewPageAsync();
        var secondPage = await browser.NewPageAsync();
        var leaderboardUrl = $"{fixture.BaseUrl}/leaderboards/{fixture.CompetitionSlug}";
        await firstPage.GotoAsync(leaderboardUrl);
        await secondPage.GotoAsync(leaderboardUrl);

        await ExpectTextAsync(firstPage, ".leaderboard-table tbody tr:first-child", "Grace Hopper");
        await ExpectTextAsync(secondPage, ".leaderboard-table tbody tr:first-child", "Grace Hopper");

        var scorerPage = await browser.NewPageAsync();
        var browserErrors = new List<string>();
        scorerPage.PageError += (_, exception) => browserErrors.Add(exception);
        scorerPage.RequestFailed += (_, request) => browserErrors.Add($"request {request.Method} {request.Url}: {request.Failure}");
        scorerPage.Response += (_, response) =>
        {
            if (response.Status >= 400)
            {
                browserErrors.Add($"response {response.Status} {response.Request.Method} {response.Url}");
            }
        };
        scorerPage.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                browserErrors.Add(message.Text);
            }
        };
        await scorerPage.GotoAsync($"{fixture.BaseUrl}/Account/Login");
        await scorerPage.Locator("input[autocomplete='username webauthn']").FillAsync(fixture.ScorerEmail);
        await scorerPage.Locator("input[type='password']").FillAsync(fixture.ScorerPassword);
        await scorerPage.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await scorerPage.GotoAsync($"{fixture.BaseUrl}/scoring");
        scorerPage.Url.Should().Contain("/scoring");
        await scorerPage.WaitForTimeoutAsync(2_000);
        await scorerPage.Locator("select#competition").SelectOptionAsync(fixture.CompetitionId.ToString());
        var participantOptions = await scorerPage.Locator("select[name='participant'] option").AllTextContentsAsync();
        if (!participantOptions.Any(option => option.Contains("Ada Lovelace", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Participant options were not loaded for competition {fixture.CompetitionId}: {string.Join(" | ", participantOptions)}");
        }
        await scorerPage.Locator("select[name='participant']").SelectOptionAsync(new SelectOptionValue { Label = "Ada Lovelace · @ada" });
        await scorerPage.Locator("input[name='points']").FillAsync("100");
        await scorerPage.Locator("textarea[name='reason']").FillAsync("Browser smoke test score");
        await scorerPage.GetByRole(AriaRole.Button, new() { Name = "Record score" }).ClickAsync();
        try
        {
            await scorerPage.GetByRole(AriaRole.Status).Filter(new() { HasText = "Score recorded" }).WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        }
        catch (TimeoutException)
        {
            var errors = await scorerPage.Locator("[role='alert']").AllTextContentsAsync();
            var body = await scorerPage.Locator("body").InnerTextAsync();
            throw new InvalidOperationException($"Score submission did not succeed at {scorerPage.Url}: {string.Join(" | ", errors)}; body={body[..Math.Min(body.Length, 300)]}; browser={string.Join(" | ", browserErrors)}; serverErrors={fixture.ServerErrors}; server={fixture.ServerLog}");
        }

        await ExpectTextAsync(firstPage, ".leaderboard-table tbody tr:first-child", "Ada Lovelace");
        await ExpectTextAsync(secondPage, ".leaderboard-table tbody tr:first-child", "Ada Lovelace");
        firstPage.Url.Should().Be(leaderboardUrl);
        secondPage.Url.Should().Be(leaderboardUrl);
    }

    private static async Task ExpectTextAsync(IPage page, string selector, string expected)
    {
        var locator = page.Locator(selector);
        await locator.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await Assertions.Expect(locator).ToContainTextAsync(expected, new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
    }
}
