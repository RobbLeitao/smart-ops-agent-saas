using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace SmartOps.Web.Tests;

/// <summary>Los endpoints de debug solo se mapean en Development.</summary>
public sealed class DebugEndpointsTests
{
    private static async Task<(HttpStatusCode Diag, HttpStatusCode Click)> ProbeAsync(string environment)
    {
        var root = Path.Combine(Path.GetDirectoryName(typeof(DebugEndpointsTests).Assembly.Location)!, "test-runs", "debug-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseEnvironment(environment);
                b.UseContentRoot(root);
                b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = $"Data Source={Path.Combine(root, "debug.db")}"
                }));
            });
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var diag = await client.GetAsync("/debug/diag");
            var click = await client.PostAsync("/api/debug/click", new StringContent(""));
            return (diag.StatusCode, click.StatusCode);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Production_DoesNotMapDebugEndpoints()
    {
        var (diag, click) = await ProbeAsync("Production");
        Assert.Equal(HttpStatusCode.NotFound, diag);
        Assert.NotEqual(HttpStatusCode.OK, click);
    }

    [Fact]
    public async Task Development_MapsDebugEndpoints()
    {
        var (diag, click) = await ProbeAsync("Development");
        Assert.Equal(HttpStatusCode.OK, diag);
        Assert.Equal(HttpStatusCode.OK, click);
    }
}
