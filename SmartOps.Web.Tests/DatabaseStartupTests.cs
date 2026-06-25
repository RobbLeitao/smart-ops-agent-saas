using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartOps.Infrastructure.Data;
using Xunit;

namespace SmartOps.Web.Tests;

public class DatabaseStartupTests : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task DatabaseStartup_CreatesSqliteDatabaseFile()
    {
        await AssertDatabaseStartupAsync("smartops.db");
    }

    [Fact]
    public async Task DatabaseStartup_CreatesSqliteDatabaseFileInConfiguredSubdirectory()
    {
        await AssertDatabaseStartupAsync(Path.Combine("data", "smartops.db"));
    }

    private static async Task AssertDatabaseStartupAsync(string relativeDataSource)
    {
        var contentRoot = CreateUniqueContentRoot();
        Directory.CreateDirectory(contentRoot);

        var dbPath = Path.Combine(contentRoot, relativeDataSource);

        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseContentRoot(contentRoot);

                b.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] =
                            $"Data Source={relativeDataSource}"
                    });
                });
            });

        try
        {
            // Trigger startup by making a real HTTP request
            var client = factory.CreateClient();
            var response = await client.GetAsync("/");
            _ = response;

            using var scope = factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            Assert.True(File.Exists(dbPath), $"Expected smartops.db at {dbPath}");

            var customerCount = await dbContext.Customers.CountAsync();
            Assert.True(customerCount > 0, "Expected at least one seeded customer in the database");
        }
        finally
        {
            await factory.DisposeAsync();
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    private static string CreateUniqueContentRoot()
    {
        return Path.Combine(
            Path.GetDirectoryName(typeof(DatabaseStartupTests).Assembly.Location)!,
            "test-runs",
            Guid.NewGuid().ToString("N"));
    }
}
