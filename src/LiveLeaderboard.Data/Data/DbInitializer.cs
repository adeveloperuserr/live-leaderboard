using LiveLeaderboard.Core.Competitions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LiveLeaderboard.Data.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.MigrateAsync();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Admin", "Scorer" })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new IdentityRole(role));
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException($"Unable to create role '{role}': {string.Join(';', result.Errors.Select(error => error.Description))}");
                }
            }
        }

        if (!configuration.GetValue<bool>("SeedDemoData") || await context.Competitions.AnyAsync())
        {
            return;
        }

        var adminEmail = configuration["Seed:AdminEmail"]
            ?? throw new InvalidOperationException("Seed:AdminEmail is required when SeedDemoData is enabled.");
        var adminPassword = configuration["Seed:AdminPassword"]
            ?? throw new InvalidOperationException("Seed:AdminPassword is required when SeedDemoData is enabled.");
        var scorerEmail = configuration["Seed:ScorerEmail"]
            ?? throw new InvalidOperationException("Seed:ScorerEmail is required when SeedDemoData is enabled.");
        var scorerPassword = configuration["Seed:ScorerPassword"]
            ?? throw new InvalidOperationException("Seed:ScorerPassword is required when SeedDemoData is enabled.");

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await EnsureUserAsync(userManager, adminEmail, adminPassword, "Admin");
        await EnsureUserAsync(userManager, scorerEmail, scorerPassword, "Scorer");

        var competition = new Competition
        {
            Slug = "opening-night",
            Name = "Opening Night Challenge",
            Description = "Development competition for the live leaderboard walkthrough.",
            Status = CompetitionStatus.Draft
        };
        competition.Participants.Add(new Participant { CompetitionId = competition.Id, DisplayName = "Ada Lovelace", Handle = "ada", SortOrder = 1 });
        competition.Participants.Add(new Participant { CompetitionId = competition.Id, DisplayName = "Grace Hopper", Handle = "grace", SortOrder = 2 });
        competition.Participants.Add(new Participant { CompetitionId = competition.Id, DisplayName = "Alan Turing", Handle = "alan", SortOrder = 3 });
        context.Competitions.Add(competition);
        await context.SaveChangesAsync();
    }

    private static async Task EnsureUserAsync(UserManager<ApplicationUser> userManager, string email, string password, string role)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException($"Unable to create development user '{email}': {string.Join(';', createResult.Errors.Select(error => error.Description))}");
            }
        }

        if (!await userManager.IsInRoleAsync(user, role))
        {
            var roleResult = await userManager.AddToRoleAsync(user, role);
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException($"Unable to assign role '{role}' to '{email}'.");
            }
        }
    }
}
