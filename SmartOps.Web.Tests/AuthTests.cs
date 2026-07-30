using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SmartOps.Web.Tests;

/// <summary>
/// Tests de autenticación y control de acceso por rol.
/// 
/// Usuarios semilla (creados en Program.cs):
///   operador@smartops.com / P@ssw0rd!  → rol: Operador
///   admin@smartops.com    / P@ssw0rd!  → rol: Auditor
///
/// Reglas de acceso esperadas:
///   /                     → requiere [Authorize]       (Operador y Auditor)
///   /diagnostics-history  → requiere [Authorize]       (Operador y Auditor)
///   /integrations         → requiere [Authorize(Roles="Auditor")]
/// </summary>
public class AuthTests : IClassFixture<AuthTestFixture>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthTests(AuthTestFixture fixture) => _factory = fixture.Factory;

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private HttpClient CreateClient(bool allowAutoRedirect = false) =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect
        });

    private static StringContent JsonBody(object obj) =>
        new(System.Text.Json.JsonSerializer.Serialize(obj), Encoding.UTF8, "application/json");

    /// Realiza el login y devuelve un client con la cookie de sesión configurada.
    /// El endpoint /account/login devuelve 200 directamente (sin redirect),
    /// por lo que funciona independientemente de AllowAutoRedirect.
    private async Task<HttpClient> AuthenticatedClientAsync(string email, string password,
        bool allowAutoRedirect = true)
    {
        var client = CreateClient(allowAutoRedirect);
        var resp = await client.PostAsync("/account/login",
            JsonBody(new { Email = email, Password = password }));
        resp.EnsureSuccessStatusCode();
        return client;
    }

    // ─── Login: credenciales válidas ─────────────────────────────────────────

    [Fact]
    public async Task Login_ConCredencialesDeOperador_Devuelve200()
    {
        var client = CreateClient();
        var resp = await client.PostAsync("/account/login",
            JsonBody(new { Email = "operador@smartops.com", Password = "P@ssw0rd!" }));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Login_ConCredencialesDeAuditor_Devuelve200()
    {
        var client = CreateClient();
        var resp = await client.PostAsync("/account/login",
            JsonBody(new { Email = "admin@smartops.com", Password = "P@ssw0rd!" }));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ─── Login: credenciales inválidas ───────────────────────────────────────

    [Fact]
    public async Task Login_ConPasswordIncorrecto_Devuelve401()
    {
        var client = CreateClient();
        var resp = await client.PostAsync("/account/login",
            JsonBody(new { Email = "operador@smartops.com", Password = "ClaveErronea99!" }));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Login_ConEmailInexistente_Devuelve401()
    {
        var client = CreateClient();
        var resp = await client.PostAsync("/account/login",
            JsonBody(new { Email = "fantasma@smartops.com", Password = "P@ssw0rd!" }));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ─── Login: campos vacíos ────────────────────────────────────────────────

    [Fact]
    public async Task Login_ConEmailVacio_Devuelve400ConMensajeDeError()
    {
        var client = CreateClient();
        var resp = await client.PostAsync("/account/login",
            JsonBody(new { Email = "", Password = "P@ssw0rd!" }));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("vac", body); // "vacíos"
    }

    [Fact]
    public async Task Login_ConPasswordVacio_Devuelve400ConMensajeDeError()
    {
        var client = CreateClient();
        var resp = await client.PostAsync("/account/login",
            JsonBody(new { Email = "operador@smartops.com", Password = "" }));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("vac", body); // "vacíos"
    }

    // ─── Acceso anónimo: siempre redirige a /login ───────────────────────────

    [Fact]
    public async Task UsuarioAnonimo_AccedeDashboard_EsRedirigidoALogin()
    {
        var resp = await CreateClient().GetAsync("/");
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Contains("/login", resp.Headers.Location?.OriginalString ?? "");
    }

    [Fact]
    public async Task UsuarioAnonimo_AccedeHistorial_EsRedirigidoALogin()
    {
        var resp = await CreateClient().GetAsync("/diagnostics-history");
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Contains("/login", resp.Headers.Location?.OriginalString ?? "");
    }

    [Fact]
    public async Task UsuarioAnonimo_AccedeIntegraciones_EsRedirigidoALogin()
    {
        var resp = await CreateClient().GetAsync("/integrations");
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Contains("/login", resp.Headers.Location?.OriginalString ?? "");
    }

    // ─── Acceso Operador ─────────────────────────────────────────────────────

    [Fact]
    public async Task Operador_AccedeDashboard_Devuelve200()
    {
        var client = await AuthenticatedClientAsync("operador@smartops.com", "P@ssw0rd!");
        var resp = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Operador_AccedeHistorial_Devuelve200()
    {
        var client = await AuthenticatedClientAsync("operador@smartops.com", "P@ssw0rd!");
        var resp = await client.GetAsync("/diagnostics-history");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Operador_AccedeIntegraciones_EsRedirigidoADashboard()
    {
        // AllowAutoRedirect=false para observar el 302 directamente
        var client = await AuthenticatedClientAsync("operador@smartops.com", "P@ssw0rd!",
            allowAutoRedirect: false);
        var resp = await client.GetAsync("/integrations");
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Equal("/", resp.Headers.Location?.OriginalString);
    }

    // ─── Acceso Auditor ───────────────────────────────────────────────────────

    [Fact]
    public async Task Auditor_AccedeDashboard_Devuelve200()
    {
        var client = await AuthenticatedClientAsync("admin@smartops.com", "P@ssw0rd!");
        var resp = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Auditor_AccedeIntegraciones_Devuelve200()
    {
        var client = await AuthenticatedClientAsync("admin@smartops.com", "P@ssw0rd!");
        var resp = await client.GetAsync("/integrations");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
