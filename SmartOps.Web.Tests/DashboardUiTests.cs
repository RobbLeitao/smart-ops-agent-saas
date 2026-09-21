using System.Threading.Tasks;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SmartOps.Web.Tests
{
    public class DashboardUiTests
    {
        [Fact]
        public async Task ClickingDetailOpensTransactionDrawer()
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
                new SmartOps.Core.Entities.Transaction { Id = 2, Amount = 49.00m, Currency = "USD", CardLast4 = "4242", ErrorMessage = "Card declined", Status = "Failed", OccurredAt = now.AddMinutes(-30) },
                new SmartOps.Core.Entities.Transaction { Id = 3, Amount = 120.00m, Currency = "EUR", CardLast4 = "1111", ErrorMessage = "Expired card", Status = "Failed", OccurredAt = now.AddHours(-2) },
                new SmartOps.Core.Entities.Transaction { Id = 4, Amount = 15.50m, Currency = "USD", CardLast4 = "2222", ErrorMessage = "Insufficient funds", Status = "Failed", OccurredAt = now.AddDays(-1) }
            });
            db.SaveChanges();

            var tcs = new System.Threading.Tasks.TaskCompletionSource<string>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
            var fake = new ControllableFakeAIOps(tcs);
            var drawerService = new SmartOps.Web.Services.TransactionDrawerService(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SmartOps.Web.Services.TransactionDrawerService>.Instance);

            ctx.Services.AddSingleton(db);
            ctx.Services.AddSingleton<SmartOps.Web.Services.IAIOpsService>(fake);
            ctx.Services.AddSingleton<SmartOps.Web.Services.DiagnosticOrchestratorService>();
            ctx.Services.AddSingleton(drawerService);
            ctx.Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider());

            var comp = ctx.Render<SmartOps.Web.Components.Pages.Dashboard>();

            // Wait for initial render
            await Task.Delay(50);

            // The dashboard no longer analyzes/spinners inline: clicking "Detalle" on a
            // failed transaction row opens the TransactionDetailDrawer via TransactionDrawerService.
            var button = comp.Find("button.btn-detail");

            Assert.Null(drawerService.Current);

            button.Click();

            Assert.NotNull(drawerService.Current);
            Assert.Equal(2, drawerService.Current!.Id);
            Assert.Equal("4242", drawerService.Current!.CardLast4);
            Assert.Equal("Failed", drawerService.Current!.Status);

            // Ensure the fake AI service task (used by other tests via the same fixture) can still complete cleanly.
            tcs.SetResult("## 🔍 Simulated result\n\n- ok");
            await Task.Delay(50);
        }

        private sealed class TestAuthenticationStateProvider : AuthenticationStateProvider
        {
            public override Task<AuthenticationState> GetAuthenticationStateAsync()
            {
                var identity = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "admin"),
                    new Claim(ClaimTypes.Name, "admin@smartops.com"),
                    new Claim(ClaimTypes.Role, "Auditor")
                }, "Test");

                return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
            }
        }
    }
}