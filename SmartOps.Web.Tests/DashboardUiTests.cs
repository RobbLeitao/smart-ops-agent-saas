using System.Threading.Tasks;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SmartOps.Web.Tests
{
    public class DashboardUiTests : TestContext
    {
        [Fact]
        public async Task ClickingAnalyzeShowsSpinner()
        {
            // Arrange
            // Register in-memory AppDbContext and a fake orchestrator
            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<SmartOps.Infrastructure.Data.AppDbContext>()
                .UseInMemoryDatabase("bunit-db")
                .Options;
            Services.AddSingleton(new SmartOps.Infrastructure.Data.AppDbContext(options));
            // Register fake IAIOps and real orchestrator wired to it
            Services.AddSingleton<SmartOps.Web.Services.IAIOpsService>(new FakeAIOps());
            Services.AddSingleton<SmartOps.Web.Services.DiagnosticOrchestratorService>();

            var comp = RenderComponent<SmartOps.Web.Components.Pages.Dashboard>();


            // Wait for initial render
            await Task.Delay(50);

            var button = comp.Find("button");
            // Act
            await button.ClickAsync();

            // The component should show a spinner while analyzing
            var spinner = comp.FindAll(".spinner-border");
            Assert.NotEmpty(spinner);
        }
    }
}
