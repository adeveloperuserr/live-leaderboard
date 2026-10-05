using FluentAssertions;
using Microsoft.Playwright;

namespace LiveLeaderboard.Tests.Browser;

public sealed class LiveLeaderboardPlaywrightTests(PlaywrightFixture fixture) : IClassFixture<PlaywrightFixture>
{
    [BrowserFact]
    public async Task Main_navigation_opens_the_public_competition_index()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();

        await page.GotoAsync(fixture.BaseUrl);
        var liveLeaderboardsLink = page.GetByRole(AriaRole.Link, new() { Name = "Live Leaderboards", Exact = true });
        (await liveLeaderboardsLink.GetAttributeAsync("href")).Should().Be("/");
    }

    [BrowserFact]
    public async Task Login_page_uses_local_account_language_without_external_provider_template_text()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();

        await page.GotoAsync($"{fixture.BaseUrl}/Account/Login");
        var pageText = await page.Locator("body").InnerTextAsync();

        pageText.Should().Contain("Use a local account to log in.");
        pageText.Should().NotContain("There are no external authentication services configured");
        pageText.Should().NotContain("setting up this ASP.NET application");
    }

    [BrowserFact]
    public async Task Split_screen_view_compacts_navigation_and_keeps_scores_visible()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = 752, Height = 1000 }
        });

       await page.GotoAsync($"{fixture.BaseUrl}/leaderboards/{fixture.CompetitionSlug}");
       await Assertions.Expect(page.Locator(".app-shell")).ToHaveCSSAsync("flex-direction", "column");
       var sidebarBox = await page.Locator(".app-sidebar").BoundingBoxAsync();
       sidebarBox.Should().NotBeNull();
       sidebarBox!.Height.Should().BeLessThan(300);
       await Assertions.Expect(page.Locator(".leaderboard-table")).ToHaveCSSAsync("min-width", "0px");
        await Assertions.Expect(page.Locator(".score-cell").First).ToBeVisibleAsync();
    }

    [BrowserFact]
    public async Task Public_pages_expose_the_live_signal_visual_shell()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();

        await page.GotoAsync(fixture.BaseUrl);
        await Assertions.Expect(page.Locator(".app-shell")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-testid='competition-index']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".competition-toolbar")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".home-hero")).ToHaveCSSAsync("background-color", "rgb(16, 27, 32)");

        await page.GotoAsync($"{fixture.BaseUrl}/leaderboards/{fixture.CompetitionSlug}");
        await Assertions.Expect(page.Locator("[data-testid='live-leaderboard']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".live-activity-banner")).ToContainTextAsync("Live updates");
    }

    [BrowserFact]
    public async Task Public_competition_card_hides_the_internal_slug()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();

        await page.GotoAsync(fixture.BaseUrl);

        await Assertions.Expect(page.Locator(".competition-card").First).ToBeVisibleAsync();
        var cardText = await page.Locator(".competition-card").First.InnerTextAsync();
        cardText.Should().Contain("Browser Smoke Challenge");
        cardText.Should().NotContain(fixture.CompetitionSlug);
    }

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
        await Assertions.Expect(scorerPage.Locator(".form-error")).ToBeHiddenAsync();
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

        var successText = await scorerPage.GetByRole(AriaRole.Status).InnerTextAsync();
        successText.Should().NotContain("Event ");

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
