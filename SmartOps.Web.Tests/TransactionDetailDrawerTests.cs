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
using SmartOps.Infrastructure.Data;
using SmartOps.Web.Components.Layout;
using SmartOps.Web.Services;
using Xunit;

namespace SmartOps.Web.Tests;

/// <summary>bUnit del Drawer de Detalle: los 4 estados de 03-drawer-detalle.html. Sin red.</summary>
public sealed class TransactionDetailDrawerTests
{
    private const string SimulatorMarkdown =
        "## 🔍 Resumen del Error\nEl emisor rechazó la tarjeta.\n\n" +
        "## 🛠️ Acciones Recomendadas para el Operador\n- Verificar con el banco.\n- Ofrecer otra tarjeta.\n\n" +
        "## 📨 Mensaje sugerido para el cliente\nHola, tu pago fue rechazado por tu banco.";

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private sealed class Harness : IDisposable
    {
        public TestContext Ctx { get; } = new();
        public AppDbContext Db { get; }
        public TransactionDrawerService Drawer { get; }
        public TaskCompletionSource<string> Ai { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IRenderedComponent<TransactionDetailDrawer> Cut { get; private set; } = null!;

        public Harness(string role, IAIOpsService? ai = null, Action<AppDbContext>? seed = null)
        {
            Ctx.JSInterop.Mode = JSRuntimeMode.Loose;

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase("drawer-" + Guid.NewGuid())
                .Options;
            Db = new AppDbContext(options);
            Db.Transactions.Add(new Transaction
            {
                Id = 1440, Amount = 67m, Currency = "USD", CardLast4 = "3723", Status = "Failed",
                ErrorMessage = "Suspected fraud", Provider = "Simulator", OccurredAt = new DateTime(2026, 11, 13, 20, 3, 0, DateTimeKind.Utc)
            });
            seed?.Invoke(Db);
            Db.SaveChanges();

            Drawer = new TransactionDrawerService(NullLogger<TransactionDrawerService>.Instance);
            var gate = new DbContextGate();

            Ctx.Services.AddSingleton(Db);
            Ctx.Services.AddSingleton(gate);
            Ctx.Services.AddSingleton(Drawer);
            Ctx.Services.AddSingleton<IAIOpsService>(ai ?? new ControllableFakeAIOps(Ai));
            Ctx.Services.AddSingleton(sp => new DiagnosticOrchestratorService(sp.GetRequiredService<IAIOpsService>(), Db));
            Ctx.Services.AddSingleton(new DiagnosticFeedbackService(Db, NullLogger<DiagnosticFeedbackService>.Instance, gate));
            Ctx.Services.AddSingleton<AuthenticationStateProvider>(new RoleAuthProvider(role));
        }

        public Harness Open(bool autoAnalyze = false)
        {
            Cut = Ctx.Render<TransactionDetailDrawer>();
            Cut.InvokeAsync(() => Drawer.Open(new TransactionDto
            {
                Id = 1440, Amount = 67m, Currency = "USD", CardLast4 = "3723", Status = "Failed",
                ErrorCode = "Suspected fraud", Channel = "Simulator", Timestamp = new DateTime(2026, 11, 13, 20, 3, 0, DateTimeKind.Utc)
            }, autoAnalyze));
            return this;
        }

        public string State => Cut.Find("section.drawer-diag").GetAttribute("data-state")!;

        public void Dispose() => Ctx.Dispose();
    }

    [Fact]
    public void Header_And_Data_FollowTheMockup()
    {
        using var h = new Harness("Operador").Open();

        h.Cut.WaitForAssertion(() => Assert.Equal("empty", h.State), Wait);

        Assert.Equal("TX 1440", h.Cut.Find("#drawer-title").TextContent);
        Assert.Contains("Fallida", h.Cut.Find(".drawer-status").TextContent);
        var close = h.Cut.Find("button.drawer-close");
        Assert.Equal("Cerrar detalle", close.GetAttribute("aria-label"));

        var aside = h.Cut.Find("aside.drawer");
        Assert.Equal("true", aside.GetAttribute("aria-modal"));
        Assert.Equal("drawer-title", aside.GetAttribute("aria-labelledby"));

        var labels = h.Cut.FindAll(".drawer-data dt").Select(e => e.TextContent).ToArray();
        Assert.Equal(new[] { "Motivo", "Monto", "Tarjeta", "Fecha", "Canal" }, labels);
        Assert.Contains("Suspected fraud", h.Cut.Find(".drawer-field-wide").TextContent);
        Assert.Contains("•••• 3723", h.Cut.Markup);

        // «Analizar con IA» ya no está en el header.
        Assert.Empty(h.Cut.Find(".drawer-header").QuerySelectorAll("button").Where(b => b.TextContent.Contains("Analizar")));
    }

    [Fact]
    public void Escape_ClosesTheDrawer()
    {
        using var h = new Harness("Operador").Open();
        h.Cut.WaitForAssertion(() => Assert.Equal("empty", h.State), Wait);

        h.Cut.Find("aside.drawer").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        Assert.Null(h.Drawer.Current);
        h.Cut.WaitForAssertion(() => Assert.Empty(h.Cut.FindAll("#drawer-title")), Wait);
    }

    [Fact]
    public void State1_WithoutDiagnostic_ShowsDashedBoxAndPrimaryButton()
    {
        using var h = new Harness("Operador").Open();

        h.Cut.WaitForAssertion(() => Assert.Equal("empty", h.State), Wait);
        Assert.Contains("Todavía no hay diagnóstico", h.Cut.Find(".drawer-empty").TextContent);
        var button = h.Cut.Find(".drawer-empty button.btn-ds-primary");
        Assert.Equal("Analizar con IA", button.TextContent.Trim());
    }

    [Fact]
    public void State2And3_AnalyzingThenReady_WithSectionsAndVote()
    {
        using var h = new Harness("Operador").Open();
        h.Cut.WaitForAssertion(() => Assert.Equal("empty", h.State), Wait);

        h.Cut.Find(".drawer-empty button").Click();

        // 2 · Analizando: recuadro IA, esqueleto violeta y botón deshabilitado.
        h.Cut.WaitForAssertion(() => Assert.Equal("analyzing", h.State), Wait);
        Assert.Contains("Analizando con IA…", h.Cut.Find(".drawer-analyzing").TextContent);
        Assert.Equal(6, h.Cut.FindAll(".drawer-ai-skeleton > span").Count);
        var busy = h.Cut.Find(".drawer-analyzing-btn");
        Assert.True(busy.HasAttribute("disabled"));
        Assert.Equal("Analizando…", busy.TextContent.Trim());

        h.Ai.SetResult(SimulatorMarkdown);

        // 3 · Listo: chip, secciones, Copiar y voto.
        h.Cut.WaitForAssertion(() => Assert.Equal("ready", h.State), Wait);
        Assert.StartsWith("Listo ·", h.Cut.Find(".status-chip-ready").TextContent.Trim());
        var titles = h.Cut.FindAll(".drawer-overline").Select(e => e.TextContent).ToArray();
        Assert.Equal(new[] { "Qué pasó", "Qué hacer", "Mensaje para el cliente" }, titles);
        Assert.Equal(2, h.Cut.FindAll(".drawer-steps ol li").Count);
        Assert.Contains("tu pago fue rechazado", h.Cut.Find(".drawer-message").TextContent);
        Assert.Contains("Copiar mensaje", h.Cut.Find(".drawer-block-message button").TextContent);
        Assert.Single(h.Db.Diagnostics);

        var yes = h.Cut.FindAll(".drawer-vote-btn").Single(b => b.TextContent == "Sí");
        Assert.Equal("false", yes.GetAttribute("aria-pressed"));
        yes.Click();

        h.Cut.WaitForAssertion(() =>
            Assert.Equal("true", h.Cut.FindAll(".drawer-vote-btn").Single(b => b.TextContent == "Sí").GetAttribute("aria-pressed")), Wait);
        var vote = Assert.Single(h.Db.DiagnosticFeedback);
        Assert.True(vote.IsUseful);
        Assert.Equal("user-Operador", vote.UserId);
    }

    [Fact]
    public void State3_ExistingDiagnostic_ShowsTheUsersPreviousVote()
    {
        var diagId = Guid.NewGuid();
        using var h = new Harness("Operador", seed: db =>
        {
            db.Diagnostics.Add(new Diagnostic { Id = diagId, TransactionId = 1440, Markdown = SimulatorMarkdown, CardLast4 = "3723", CreatedAt = DateTime.UtcNow });
            db.DiagnosticFeedback.Add(new DiagnosticFeedback { Id = Guid.NewGuid(), DiagnosticId = diagId, UserId = "user-Operador", IsUseful = false });
        }).Open();

        h.Cut.WaitForAssertion(() =>
        {
            Assert.Equal("ready", h.State);
            Assert.Equal("true", h.Cut.FindAll(".drawer-vote-btn").Single(b => b.TextContent == "No").GetAttribute("aria-pressed"));
            Assert.Equal("false", h.Cut.FindAll(".drawer-vote-btn").Single(b => b.TextContent == "Sí").GetAttribute("aria-pressed"));
        }, Wait);
    }

    [Fact]
    public void State3_UnrecognizedFormat_RendersFullMarkdownAsHtml()
    {
        using var h = new Harness("Operador", seed: db =>
            db.Diagnostics.Add(new Diagnostic { Id = Guid.NewGuid(), TransactionId = 1440, Markdown = "Falló por **fondos**.", CardLast4 = "3723" })).Open();

        h.Cut.WaitForAssertion(() => Assert.Equal("ready", h.State), Wait);
        Assert.Empty(h.Cut.FindAll(".drawer-overline"));
        Assert.Contains("<strong>fondos</strong>", h.Cut.Find(".drawer-diag .drawer-md").InnerHtml);
    }

    [Theory]
    [InlineData("Auditor", true)]
    [InlineData("Operador", false)]
    public void State4_ProviderError_ShowsAlertRetryAndRoleSpecificHelp(string role, bool expectLink)
    {
        var failing = new ThrowingAIOps(new AiEngineException("El proveedor de IA devolvió un error (404). Verificá el modelo y el endpoint en Integraciones."));
        using var h = new Harness(role, failing).Open(autoAnalyze: true);

        h.Cut.WaitForAssertion(() => Assert.Equal("error", h.State), Wait);

        var alert = h.Cut.Find(".drawer-alert[role=alert]");
        Assert.Contains("No se pudo generar el diagnóstico", alert.TextContent);
        Assert.Contains("(404)", alert.TextContent);
        Assert.Equal("Reintentar", h.Cut.Find(".drawer-actions button.btn-ds-primary").TextContent.Trim());
        Assert.Contains("No se guardó nada en el Historial.", h.Cut.Markup);
        Assert.Empty(h.Db.Diagnostics);

        Assert.Equal(expectLink, h.Cut.FindAll("a[href='/integrations']").Count == 1);
        Assert.Equal(!expectLink, h.Cut.Markup.Contains("Avisale a un Auditor para revisar la configuración"));
        Assert.Equal(1, failing.Calls);

        // Reintentar vuelve a llamar al motor (y vuelve a fallar de forma confiable).
        h.Cut.Find(".drawer-actions button.btn-ds-primary").Click();
        h.Cut.WaitForAssertion(() => { Assert.Equal(2, failing.Calls); Assert.Equal("error", h.State); }, Wait);
    }

    [Fact]
    public void AutoAnalyze_StartsAnalysisWhenOpenedFromTheDashboard()
    {
        using var h = new Harness("Operador").Open(autoAnalyze: true);
        h.Cut.WaitForAssertion(() => Assert.Equal("analyzing", h.State), Wait);
        h.Ai.SetResult(SimulatorMarkdown);
        h.Cut.WaitForAssertion(() => Assert.Equal("ready", h.State), Wait);
    }

    private sealed class ThrowingAIOps : IAIOpsService
    {
        private readonly Exception _ex;
        public int Calls { get; private set; }
        public ThrowingAIOps(Exception ex) => _ex = ex;
        public Task<string> ExecutePromptAsync(string userPrompt)
        {
            Calls++;
            return Task.FromException<string>(_ex);
        }
    }

    private sealed class RoleAuthProvider : AuthenticationStateProvider
    {
        private readonly string _role;
        public RoleAuthProvider(string role) => _role = role;

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user-" + _role),
                new Claim(ClaimTypes.Name, _role.ToLowerInvariant() + "@smartops.com"),
                new Claim(ClaimTypes.Role, _role)
            }, "Test");
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }
}
