using System.Threading.Tasks;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SmartOps.Web.Components.Layout;
using SmartOps.Web.Services;
using Xunit;

namespace SmartOps.Web.Tests;

public sealed class DashboardHeaderToggleTests
{
    [Fact]
    public void ShowsConnectionStates_WithPausedTakingPriority()
    {
        using var ctx = new TestContext();
        var header = new DashboardHeaderActionsService();
        ctx.Services.AddSingleton(header);

        var comp = ctx.Render<DashboardHeaderToggle>(p => p.Add(x => x.OnDashboard, true));

        // Primer frame en el Dashboard, antes de que se registre: «Conectando…» con punto vacío.
        Assert.Contains("Conectando…", comp.Find(".live-status").TextContent);
        Assert.Contains("is-connecting", comp.Find(".live-status").ClassName);

        header.Register(() => Task.CompletedTask, isPaused: false);
        comp.WaitForAssertion(() => Assert.Contains("Conectando…", comp.Find(".live-status").TextContent));

        header.SetConnectionState(LiveConnectionState.Live);
        comp.WaitForAssertion(() => Assert.Contains("En vivo", comp.Find(".live-status").TextContent));
        Assert.DoesNotContain("is-connecting", comp.Find(".live-status").ClassName);

        header.SetConnectionState(LiveConnectionState.Reconnecting);
        comp.WaitForAssertion(() => Assert.Contains("Reconectando…", comp.Find(".live-status").TextContent));

        header.UpdateState(isPaused: true);
        comp.WaitForAssertion(() => Assert.Contains("Pausado", comp.Find(".live-status").TextContent));
    }

    [Fact]
    public void HiddenOutsideDashboard_AndAfterDashboardLeaves()
    {
        using var ctx = new TestContext();
        var header = new DashboardHeaderActionsService();
        ctx.Services.AddSingleton(header);

        var other = ctx.Render<DashboardHeaderToggle>(p => p.Add(x => x.OnDashboard, false));
        Assert.Empty(other.FindAll(".live-status"));

        var comp = ctx.Render<DashboardHeaderToggle>(p => p.Add(x => x.OnDashboard, true));
        header.Register(() => Task.CompletedTask, false);
        header.Unregister();
        comp.WaitForAssertion(() => Assert.Empty(comp.FindAll(".live-status")));
    }
}
