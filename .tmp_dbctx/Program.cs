using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using SmartOps.Infrastructure.Data;
using System.Threading.Tasks;

class Program
{
    static async Task<int> Main()
    {
        try
        {
            var services = new ServiceCollection();
            // Read connection string from SmartOps.Web appsettings.json fallback
            var conn = $"Data Source=..\\SmartOps.Web\\smartops.db";
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(conn));
            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var txs = await db.Transactions.ToListAsync();
            Console.WriteLine("Id\tCustomerId\tAmount\tCurrency\tStatus\tGatewayRef\tProvider\tErrorMessage\tOccurredAt");
            foreach (var t in txs)
            {
                Console.WriteLine($"{t.Id}\t{t.CustomerId}\t{t.Amount}\t{t.Currency}\t{t.Status}\t{t.GatewayReference}\t{t.Provider}\t{t.ErrorMessage}\t{t.OccurredAt:o}");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR: " + ex);
            return 1;
        }
    }
}
