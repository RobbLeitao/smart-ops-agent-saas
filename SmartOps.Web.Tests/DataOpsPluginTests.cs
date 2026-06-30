using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SmartOps.Web.Plugins;
using Xunit;

namespace SmartOps.Web.Tests;

public sealed class DataOpsPluginTests
{
    [Fact]
    public async Task GetFailedTransactionsAsync_ReturnsFormattedStripeFailures()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var scope = factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<SmartOps.Infrastructure.Data.AppDbContext>();
        // Ensure database is deleted and recreated to pick up model changes and seed data
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();

        var plugin = new DataOpsPlugin(db);

        var result = await plugin.GetFailedTransactionsAsync();

        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.Contains("Id: 2", result);
        Assert.Contains("Amount:", result);
        Assert.Contains("Card declined", result);
        Assert.Contains("OccurredAt:", result);
    }
}
