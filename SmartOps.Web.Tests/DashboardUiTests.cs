using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartOps.Core.Entities;
using SmartOps.Infrastructure;
using SmartOps.Infrastructure.Data;
using SmartOps.Web.Services;
using Xunit;

namespace SmartOps.Web.Tests
{
    public class DashboardUiTests
    {
        private static (TestContext ctx, TransactionDrawerService drawer) CreateContext(Action<AppDbContext> seed)
        {
            var ctx = new TestContext();
            ctx.JSInterop.Mode = JSRuntimeMode.Loose;

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase("bunit-db" + Guid.NewGuid())
                .Options;
            var db = new AppDbContext(options);
            seed(db);
            db.SaveChanges();

            var drawerService = new TransactionDrawerService(NullLogger<TransactionDrawerService>.Instance);

            ctx.Services.AddSingleton(db);
            ctx.Services.AddSingleton<DbContextGate>();
            ctx.Services.AddSingleton(drawerService);
            ctx.Services.AddSingleton<DashboardHeaderActionsService>();
            ctx.Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider());
            ctx.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new TransactionSettings { IntervalSeconds = 5, Provider = "Simulator" }));
            return (ctx, drawerService);
        }

        private static void SeedFailed(AppDbContext db)
        {
            var now = DateTime.UtcNow;
            db.Transactions.AddRange(
                new Transaction { Id = 2, Amount = 49.00m, Currency = "USD", CardLast4 = "4242", ErrorMessage = "Card declined", Status = "Failed", OccurredAt = now.AddMinutes(-30) },
                new Transaction { Id = 3, Amount = 120.00m, Currency = "EUR", CardLast4 = "1111", ErrorMessage = "Expired card", Status = "Failed", OccurredAt = now.AddHours(-2) },
                new Transaction { Id = 4, Amount = 15.50m, Currency = "USD", CardLast4 = "2222", ErrorMessage = "Insufficient funds", Status = "Failed", OccurredAt = now.AddDays(-1) },
                new Transaction { Id = 5, Amount = 10m, Currency = "USD", CardLast4 = "3333", Status = "Succeeded", OccurredAt = now });
        }

        [Fact]
        public void ShowsHeaderAndIntervalFromSettings()
        {
            var (ctx, _) = CreateContext(SeedFailed);
            using var _ctx = ctx;

            var comp = ctx.Render<SmartOps.Web.Components.Pages.Dashboard>();

            Assert.Equal("Centro de operaciones", comp.Find("h1").TextContent);
            comp.WaitForAssertion(() => Assert.Contains("actualiza cada 5 s", comp.Markup), TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void PendingRow_AnalyzeOpensDrawerWithAutoAnalyze()
        {
            var (ctx, drawer) = CreateContext(SeedFailed);
            using var _ctx = ctx;

            var comp = ctx.Render<SmartOps.Web.Components.Pages.Dashboard>();
            comp.WaitForElement("button[aria-label='Analizar transacción 2']", TimeSpan.FromSeconds(5)).Click();

            Assert.NotNull(drawer.Current);
            Assert.Equal(2, drawer.Current!.Id);
            Assert.Equal("4242", drawer.Current.CardLast4);
            Assert.True(drawer.TryConsumeAutoAnalyze(2));
        }

        [Fact]
        public void DiagnosedRow_ShowsListoAndVerOpensWithoutAnalyzing()
        {
            var (ctx, drawer) = CreateContext(db =>
            {
                SeedFailed(db);
                db.Diagnostics.Add(new Diagnostic { Id = Guid.NewGuid(), TransactionId = 3, Markdown = "## Resumen\nx" });
            });
            using var _ctx = ctx;

            var comp = ctx.Render<SmartOps.Web.Components.Pages.Dashboard>();
            var ver = comp.WaitForElement("button[aria-label='Ver transacción 3']", TimeSpan.FromSeconds(5));
            Assert.Contains("Listo", ver.ParentElement!.ParentElement!.TextContent);

            ver.Click();
            Assert.Equal(3, drawer.Current!.Id);
            Assert.False(drawer.TryConsumeAutoAnalyze(3));
        }

        [Fact]
        public void AnalyzeMostRecent_PicksNewestPending()
        {
            var (ctx, drawer) = CreateContext(SeedFailed);
            using var _ctx = ctx;

            var comp = ctx.Render<SmartOps.Web.Components.Pages.Dashboard>();
            var button = comp.WaitForElement("#analyze-most-recent", TimeSpan.FromSeconds(5));
            Assert.Contains("Analizar la más reciente", button.TextContent);
            Assert.Contains("3", comp.Find("#queue-pending-count").TextContent);

            button.Click();
            Assert.Equal(2, drawer.Current!.Id);
            Assert.True(drawer.TryConsumeAutoAnalyze(2));
        }

        [Fact]
        public void AnalyzeMostRecent_DisabledWhenNothingPending()
        {
            var (ctx, _) = CreateContext(db =>
            {
                db.Transactions.Add(new Transaction { Id = 7, Status = "Failed", ErrorMessage = "Card declined", CardLast4 = "1234", OccurredAt = DateTime.UtcNow });
                db.Diagnostics.Add(new Diagnostic { Id = Guid.NewGuid(), TransactionId = 7, Markdown = "x" });
            });
            using var _ctx = ctx;

            var comp = ctx.Render<SmartOps.Web.Components.Pages.Dashboard>();
            comp.WaitForAssertion(() =>
            {
                var button = comp.Find("#analyze-most-recent");
                Assert.True(button.HasAttribute("disabled"));
                Assert.Equal("No hay pendientes", button.TextContent.Trim());
            }, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void Table_Paginates20RowsPerPage()
        {
            var (ctx, _) = CreateContext(db =>
            {
                var now = DateTime.UtcNow;
                for (var i = 1; i <= 45; i++)
                {
                    db.Transactions.Add(new Transaction { Id = i, Status = "Failed", ErrorMessage = "Card declined", CardLast4 = "1234", Currency = "USD", OccurredAt = now.AddMinutes(-i) });
                }
            });
            using var _ctx = ctx;

            var comp = ctx.Render<SmartOps.Web.Components.Pages.Dashboard>();
            comp.WaitForAssertion(() => Assert.Equal(20, comp.FindAll("table tbody tr").Count), TimeSpan.FromSeconds(5));
            Assert.Contains("Página 1 de 3", comp.Markup);

            comp.FindAll(".dash-pager button").Single(b => b.TextContent.Contains("Siguiente")).Click();
            comp.FindAll(".dash-pager button").Single(b => b.TextContent.Contains("Siguiente")).Click();

            Assert.Equal(5, comp.FindAll("table tbody tr").Count);
            Assert.Contains("Página 3 de 3", comp.Markup);
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
