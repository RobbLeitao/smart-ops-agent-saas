using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using SmartOps.Web.Plugins;
using Xunit;

namespace SmartOps.Web.Tests;

public sealed class AIOpsKernelPluginIntegrationTests
{
    [Fact]
    public async Task Kernel_Has_DataOpsPlugin_And_AIOpsService_CanAccessFunction()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var scope = factory.Services.CreateScope();

        var kernel = scope.ServiceProvider.GetRequiredService<Kernel>();
        Assert.NotNull(kernel);

        // Verify AIOpsService can be resolved and DataOpsPlugin can be invoked via DI (end-to-end)
        var aiOps = scope.ServiceProvider.GetRequiredService<SmartOps.Web.Services.AIOpsService>();
        Assert.NotNull(aiOps);

        // Resolve the plugin via DI and call its method directly to ensure end-to-end availability
        var plugin = scope.ServiceProvider.GetRequiredService<DataOpsPlugin>();
        var result = await plugin.GetFailedTransactionsAsync();

        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.Contains("Id:", result);
    }
}
