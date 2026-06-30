using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using SmartOps.Web.Plugins;
using Xunit;

namespace SmartOps.Web.Tests;

public sealed class DataOpsPluginTests
{
    [Fact]
    public async Task GetFailedTransactionsAsync_ReturnsFormattedStripeFailures()
    {
        // Create an isolated in-memory AppDbContext for the unit test to avoid DI/provider conflicts
        var options = new DbContextOptionsBuilder<SmartOps.Infrastructure.Data.AppDbContext>()
            .UseInMemoryDatabase("DataOpsPluginDb" + Guid.NewGuid())
            .Options;

        using var db = new SmartOps.Infrastructure.Data.AppDbContext(options);
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
