using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartOps.Infrastructure.Data;
using Xunit;

namespace SmartOps.Web.Tests;

public class DatabaseStartupTests : IAsyncLifetime
{
    private readonly string _contentRoot;
    private WebApplicationFactory<Program> _factory = null!;

    public DatabaseStartupTests()
    {
        // Unique per-run directory eliminates any cross-run false positives.
        _contentRoot = Path.Combine(
            Path.GetDirectoryName(typeof(DatabaseStartupTests).Assembly.Location)!,
            "test-runs",
            Guid.NewGuid().ToString("N"));
    }

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_contentRoot);
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseContentRoot(_contentRoot));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        if (Directory.Exists(_contentRoot))
            Directory.Delete(_contentRoot, recursive: true);
    }

    [Fact]
    public async Task DatabaseStartup_CreatesSqliteDatabaseFile()
    {
        // Trigger startup by making a real HTTP request
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/");
        _ = response; // startup side-effects are all we need here

        // AppDbContext must be resolvable — this will fail until Program.cs wires it up
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The SQLite file must exist in this run's unique content root — never a leftover.
        var dbPath = Path.Combine(_contentRoot, "smartops.db");
        Assert.True(File.Exists(dbPath), $"Expected smartops.db at {dbPath}");

        // Seed data must be present
        var customerCount = await dbContext.Customers.CountAsync();
        Assert.True(customerCount > 0, "Expected at least one seeded customer in the database");
    }
}
