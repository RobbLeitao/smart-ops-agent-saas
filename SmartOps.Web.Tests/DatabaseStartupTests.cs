using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartOps.Infrastructure.Data;
using Xunit;

namespace SmartOps.Web.Tests;

public class DatabaseStartupTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DatabaseStartupTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
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

        // The SQLite file should exist on disk after EnsureCreated / Migrate
        var dbPath = Path.Combine(AppContext.BaseDirectory, "smartops.db");
        Assert.True(File.Exists(dbPath), $"Expected smartops.db at {dbPath}");

        // Seed data must be present
        var customerCount = await dbContext.Customers.CountAsync();
        Assert.True(customerCount > 0, "Expected at least one seeded customer in the database");
    }
}
