using System.Threading.Tasks;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SmartOps.Web.Tests
{
    public class DashboardUiTests
    {
        [Fact]
        public async Task ClickingAnalyzeShowsSpinner()
        {
            using var ctx = new TestContext();

            // Arrange: in-memory DB
            var options = new DbContextOptionsBuilder<SmartOps.Infrastructure.Data.AppDbContext>()
                .UseInMemoryDatabase("bunit-db" + Guid.NewGuid())
                .Options;

            var db = new SmartOps.Infrastructure.Data.AppDbContext(options);
            // Seed sample failed transactions
            var now = System.DateTime.UtcNow;
            db.Transactions.AddRange(new[] {
                new SmartOps.Core.Entities.Transaction { Id = 2, Amount = 49.00m, Currency = "USD", ErrorMessage = "Card declined", Status = "Failed", OccurredAt = now.AddMinutes(-30) },
                new SmartOps.Core.Entities.Transaction { Id = 3, Amount = 120.00m, Currency = "EUR", ErrorMessage = "Expired card", Status = "Failed", OccurredAt = now.AddHours(-2) },
                new SmartOps.Core.Entities.Transaction { Id = 4, Amount = 15.50m, Currency = "USD", ErrorMessage = "Insufficient funds", Status = "Failed", OccurredAt = now.AddDays(-1) }
            });
            db.SaveChanges();

            // Create a controllable TaskCompletionSource so the orchestrator task stays pending while we assert spinner
            var tcs = new System.Threading.Tasks.TaskCompletionSource<string>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
            var fake = new ControllableFakeAIOps(tcs);

            ctx.Services.AddSingleton(db);
            ctx.Services.AddSingleton<SmartOps.Web.Services.IAIOpsService>(fake);
            ctx.Services.AddSingleton<SmartOps.Web.Services.DiagnosticOrchestratorService>();

            var comp = ctx.Render<SmartOps.Web.Components.Pages.Dashboard>();

            // Wait for initial render
            await Task.Delay(50);

            var button = comp.Find("button");

            // Act: trigger analysis (the orchestrator will await the fake's Task which we control)
            button.Click();

            // Assert spinner visible while task is pending
            comp.WaitForAssertion(() => Assert.NotEmpty(comp.FindAll(".spinner-border")), timeout: System.TimeSpan.FromSeconds(2));

            // Now complete the operation and ensure UI updates without throwing
            tcs.SetResult("## 🔍 Simulated result\n\n- ok");

            // Wait for completion and final render
            comp.WaitForAssertion(() => Assert.Contains("Simulated result", comp.Markup), timeout: System.TimeSpan.FromSeconds(2));
        }
    }
}