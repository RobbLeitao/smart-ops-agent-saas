using Microsoft.EntityFrameworkCore;
using SmartOps.Core.Entities;

namespace SmartOps.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<OperationLog> OperationLogs => Set<OperationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Customer>().HasData(
            new Customer
            {
                Id = 1,
                Name = "Contoso Retail",
                Type = "Regular"
            });

        modelBuilder.Entity<Transaction>().HasData(
            new Transaction
            {
                Id = 1,
                CustomerId = 1,
                Amount = 125.00m,
                Currency = "USD",
                Status = "Succeeded",
                GatewayReference = "TXN_OK_1001"
            },
            new Transaction
            {
                Id = 2,
                CustomerId = 1,
                Amount = 49.00m,
                Currency = "USD",
                Status = "Failed",
                GatewayReference = "TXN_ERR_502"
            });

        modelBuilder.Entity<OperationLog>().HasData(
            new OperationLog
            {
                Id = 1,
                Level = "Error",
                Component = "StripeGateway",
                Message = "Webhook timeout for transaction TXN_ERR_502. Payment was captured at gateway but status update failed locally."
            });
    }
}
