using Microsoft.EntityFrameworkCore;
using SmartOps.Core.Entities;
using SmartOps.Infrastructure.Data;
using Xunit;

namespace SmartOps.Infrastructure.Tests;

public class AppDbContextTests
{
    [Fact]
    public void AppDbContext_seeds_the_failure_scenario()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var context = new AppDbContext(options);

        context.Database.OpenConnection();
        context.Database.EnsureCreated();

        var customer = context.Customers.Single();
        var transactions = context.Transactions.OrderBy(transaction => transaction.Id).ToList();
        var log = context.OperationLogs.Single();

        Assert.Equal(1, customer.Id);
        Assert.Equal("Contoso Retail", customer.Name);
        Assert.Equal("Regular", customer.Type);

        Assert.Collection(transactions,
            transaction =>
            {
                Assert.Equal(1, transaction.Id);
                Assert.Equal(1, transaction.CustomerId);
                Assert.Equal(125.00m, transaction.Amount);
                Assert.Equal("USD", transaction.Currency);
                Assert.Equal("Succeeded", transaction.Status);
                Assert.Equal("TXN_OK_1001", transaction.GatewayReference);
            },
            transaction =>
            {
                Assert.Equal(2, transaction.Id);
                Assert.Equal(1, transaction.CustomerId);
                Assert.Equal(49.00m, transaction.Amount);
                Assert.Equal("USD", transaction.Currency);
                Assert.Equal("Failed", transaction.Status);
                Assert.Equal("TXN_ERR_502", transaction.GatewayReference);
            });

        Assert.Equal(1, log.Id);
        Assert.Equal("Error", log.Level);
        Assert.Equal("StripeGateway", log.Component);
        Assert.Equal(
            "Webhook timeout for transaction TXN_ERR_502. Payment was captured at gateway but status update failed locally.",
            log.Message);
    }
}
