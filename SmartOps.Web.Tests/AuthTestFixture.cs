using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace SmartOps.Web.Tests;

/// <summary>
/// Crea y comparte una única instancia de WebApplicationFactory con SQLite real
/// para todos los tests de autenticación. El startup siembra los usuarios de prueba.
/// </summary>
public sealed class AuthTestFixture : IAsyncLifetime
{
    private string _contentRoot = null!;
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _contentRoot = Path.Combine(
            Path.GetDirectoryName(typeof(AuthTestFixture).Assembly.Location)!,
            "test-runs",
            "auth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_contentRoot);

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseContentRoot(_contentRoot);
                b.ConfigureAppConfiguration((_, cfg) =>
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = "Data Source=smartops-auth-test.db"
                    }));
            });

        // Trigger startup: crea la DB, corre EnsureCreated y siembra operador + auditor
        using var warmup = Factory.CreateClient();
        await warmup.GetAsync("/login");
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        if (Directory.Exists(_contentRoot))
            Directory.Delete(_contentRoot, recursive: true);
    }
}
