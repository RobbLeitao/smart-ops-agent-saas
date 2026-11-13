using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartOps.Core.Entities;
using SmartOps.Infrastructure.Data;
using SmartOps.Web.Components.Pages;
using SmartOps.Web.Services;
using Xunit;

namespace SmartOps.Web.Tests;

/// <summary>bUnit del Historial (04-historial.html): selección, paginación, panel y voto. Sin red.</summary>
public sealed class DiagnosticsHistoryPageTests : IDisposable
{
    private const string AuditorId = "auditor-id";
    private const string OperatorId = "operator-id";
    private const string Markdown =
        "## 🔍 Resumen del Error\nLa tarjeta está vencida.\n\n" +
        "## 🛠️ Acciones Recomendadas para el Operador\n- Pedir otra tarjeta.\n\n" +
        "## 📨 Mensaje sugerido para el cliente\nHola, tu tarjeta está vencida.";

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private readonly SqliteConnection _connection;
    private readonly TestContext _ctx = new();

    public DiagnosticsHistoryPageTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using (var db = CreateDb())
        {
            db.Database.EnsureCreated();
            db.Users.AddRange(
                new ApplicationUser { Id = AuditorId, UserName = "admin@smartops.com", Email = "admin@smartops.com" },
                new ApplicationUser { Id = OperatorId, UserName = "operador@smartops.com", Email = "operador@smartops.com" });
            db.SaveChanges();
        }

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var pageDb = CreateDb();
        var gate = new DbContextGate();
        _ctx.Services.AddSingleton(pageDb);
        _ctx.Services.AddSingleton(gate);
        _ctx.Services.AddSingleton(new DiagnosticFeedbackService(pageDb, NullLogger<DiagnosticFeedbackService>.Instance, gate));
        _ctx.Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        _ctx.Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        _ctx.Services.AddSingleton(sp => new DiagnosticReportService(pageDb, gate, sp.GetRequiredService<IServiceScopeFactory>(), NullLoggerFactory.Instance));
    }

    public void Dispose()
    {
        _ctx.Dispose();
        _connection.Dispose();
    }

    private AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    private Guid[] Seed(int count, string createdBy = OperatorId)
    {
        using var db = CreateDb();
        var start = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        var ids = new Guid[count];
        for (var i = 0; i < count; i++)
        {
            // ids[0] es el más reciente (el primero de la tabla).
            ids[i] = Guid.NewGuid();
            db.Transactions.Add(new Transaction { Id = 2000 + i, CustomerId = 1, Amount = 10, Currency = "USD", Status = "Failed", ErrorMessage = "Expired card", Provider = "Simulator", CardLast4 = "6403" });
            db.Diagnostics.Add(new Diagnostic
            {
                Id = ids[i], TransactionId = 2000 + i, CardLast4 = (6400 + i % 10).ToString(), CreatedByUserId = createdBy,
                Markdown = Markdown, CreatedAt = start.AddMinutes(count - i)
            });
        }
        db.SaveChanges();
        return ids;
    }

    private IRenderedComponent<DiagnosticsHistory> Render(string role = "Auditor")
    {
        _ctx.Services.AddSingleton<AuthenticationStateProvider>(new RoleAuthProvider(role, role == "Auditor" ? AuditorId : OperatorId));
        var cut = _ctx.Render<DiagnosticsHistory>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")), Wait);
        return cut;
    }

    private static string SelectedTx(IRenderedComponent<DiagnosticsHistory> cut)
    {
        var row = cut.Find("tr[aria-selected=true]");
        return row.QuerySelectorAll("td")[1].TextContent.Trim();
    }

    [Fact]
    public void FirstDiagnostic_IsSelectedOnEntry_AndPanelShowsIt()
    {
        Seed(3);
        var cut = Render();

        Assert.Equal("Historial de diagnósticos", cut.Find("h1").TextContent);
        Assert.Contains("3 diagnósticos generados por el motor. Elegí uno para ver el detalle.", cut.Find(".hist-subtitle").TextContent);

        var headers = cut.FindAll("thead th").Select(th => th.TextContent).ToArray();
        Assert.Equal(new[] { "Fecha", "TX", "Tarjeta", "Motivo", "Operador", "Valoración" }, headers);

        Assert.Single(cut.FindAll("tr[aria-selected=true]"));
        Assert.Equal("2000", SelectedTx(cut));
        Assert.Equal("true", cut.Find("tr[aria-selected=true] a").GetAttribute("aria-current"));
        Assert.Equal("operador", cut.Find("tr[aria-selected=true] td:nth-child(5)").TextContent.Trim());
        Assert.Equal("Sin votar", cut.Find("tr[aria-selected=true] td:nth-child(6)").TextContent.Trim());

        cut.WaitForAssertion(() => Assert.Contains("La tarjeta está vencida.", cut.Find(".hist-panel").TextContent), Wait);
        Assert.Equal("Diagnóstico de TX 2000", cut.Find("#hist-panel-title").TextContent);
        Assert.Contains("operador@smartops.com", cut.Find(".hist-panel-data").TextContent);
        Assert.Equal(new[] { "Qué pasó", "Qué hacer", "Mensaje para el cliente" }, cut.FindAll(".hist-panel .diag-overline").Select(e => e.TextContent));
        Assert.Contains("Copiar mensaje", cut.Find(".hist-panel .diag-block-message button").TextContent);
        Assert.Contains("Descargar PDF", cut.Find(".hist-panel-foot").TextContent);
        Assert.Contains("Exportar JSON", cut.Find(".hist-panel-foot").TextContent);
    }

    [Fact]
    public void ClickingARow_SelectsIt()
    {
        Seed(3);
        var cut = Render();

        cut.FindAll("tbody tr")[2].Click();

        cut.WaitForAssertion(() => Assert.Equal("2002", SelectedTx(cut)), Wait);
        Assert.Single(cut.FindAll("tr[aria-selected=true]"));
        Assert.Null(cut.FindAll("tbody tr")[0].QuerySelector("a")!.GetAttribute("aria-current"));
        cut.WaitForAssertion(() => Assert.Equal("Diagnóstico de TX 2002", cut.Find("#hist-panel-title").TextContent), Wait);

        // El link de la primera celda (teclado) también selecciona.
        cut.FindAll("tbody tr")[1].QuerySelector("a")!.Click();
        cut.WaitForAssertion(() => Assert.Equal("2001", SelectedTx(cut)), Wait);
    }

    [Fact]
    public void Pagination_20PerPage_AndSelectionMovesToFirstVisible()
    {
        Seed(25);
        var cut = Render();

        Assert.Equal(20, cut.FindAll("tbody tr").Count);
        Assert.Contains("Mostrando 1–20 de 25", cut.Find(".hist-pager").TextContent);
        var prev = cut.FindAll(".hist-pager button").Single(b => b.TextContent == "Anterior");
        Assert.True(prev.HasAttribute("disabled"));

        cut.FindAll(".hist-pager button").Single(b => b.TextContent == "Siguiente").Click();

        cut.WaitForAssertion(() => Assert.Contains("Mostrando 21–25 de 25", cut.Find(".hist-pager").TextContent), Wait);
        Assert.Equal(5, cut.FindAll("tbody tr").Count);
        Assert.Equal("2020", SelectedTx(cut));
        Assert.True(cut.FindAll(".hist-pager button").Single(b => b.TextContent == "Siguiente").HasAttribute("disabled"));
        cut.WaitForAssertion(() => Assert.Equal("Diagnóstico de TX 2020", cut.Find("#hist-panel-title").TextContent), Wait);
    }

    [Fact]
    public void Filter_KeepsSelectionIfVisible_ElseFirstVisible()
    {
        Seed(3); // tarjetas 6400, 6401, 6402
        var cut = Render();
        cut.FindAll("tbody tr")[1].Click();
        cut.WaitForAssertion(() => Assert.Equal("2001", SelectedTx(cut)), Wait);

        cut.Find("input.hist-input-card").Input("640");
        cut.WaitForAssertion(() => Assert.Equal("2001", SelectedTx(cut)), Wait);

        cut.Find("input.hist-input-card").Input("6402");
        cut.WaitForAssertion(() => Assert.Equal(1, cut.FindAll("tbody tr").Count), Wait);
        Assert.Equal("2002", SelectedTx(cut));
        Assert.Contains("1 diagnóstico generado por el motor", cut.Find(".hist-subtitle").TextContent);
    }

    [Fact]
    public async Task Vote_MarksAriaPressed_Persists_AndUpdatesTheRow()
    {
        var ids = Seed(2);
        var cut = Render();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".hist-vote-btn").Count), Wait);

        var yes = cut.FindAll(".hist-vote-btn").Single(b => b.TextContent == "Sí");
        Assert.Equal("false", yes.GetAttribute("aria-pressed"));
        Assert.DoesNotContain("👍", cut.Markup);
        Assert.DoesNotContain("Gracias", cut.Markup);

        yes.Click();
        cut.WaitForAssertion(() =>
            Assert.Equal("true", cut.FindAll(".hist-vote-btn").Single(b => b.TextContent == "Sí").GetAttribute("aria-pressed")), Wait);
        Assert.Equal("Útil", cut.Find("tr[aria-selected=true] td:nth-child(6)").TextContent.Trim());

        // Se puede cambiar el voto.
        cut.FindAll(".hist-vote-btn").Single(b => b.TextContent == "No").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true", cut.FindAll(".hist-vote-btn").Single(b => b.TextContent == "No").GetAttribute("aria-pressed"));
            Assert.Equal("false", cut.FindAll(".hist-vote-btn").Single(b => b.TextContent == "Sí").GetAttribute("aria-pressed"));
        }, Wait);
        Assert.Equal("No útil", cut.Find("tr[aria-selected=true] td:nth-child(6)").TextContent.Trim());

        await using var check = CreateDb();
        var feedback = Assert.Single(check.DiagnosticFeedback);
        Assert.Equal((ids[0], AuditorId, false), (feedback.DiagnosticId, feedback.UserId, feedback.IsUseful));
    }

    [Fact]
    public async Task Panel_ShowsTheCurrentVote_WhenSelecting()
    {
        var ids = Seed(2);
        await using (var db = CreateDb())
        {
            db.DiagnosticFeedback.Add(new DiagnosticFeedback { Id = Guid.NewGuid(), DiagnosticId = ids[1], UserId = AuditorId, IsUseful = false });
            await db.SaveChangesAsync();
        }

        var cut = Render();
        cut.FindAll("tbody tr")[1].Click();

        cut.WaitForAssertion(() =>
            Assert.Equal("true", cut.FindAll(".hist-vote-btn").Single(b => b.TextContent == "No").GetAttribute("aria-pressed")), Wait);
        Assert.Equal("No útil", cut.Find("tr[aria-selected=true] td:nth-child(6)").TextContent.Trim());
    }

    [Fact]
    public void OnlyMine_IsVisibleForAuditor_NotForOperator()
    {
        Seed(2);
        var auditor = Render("Auditor");
        Assert.Contains("Solo los míos", auditor.Find(".hist-filters").TextContent);
        Assert.Single(auditor.FindAll(".hist-filters input[type=checkbox]"));
        // Todos los controles de filtro tienen su <label>.
        Assert.All(auditor.FindAll(".hist-filters input, .hist-filters select"), c => Assert.Equal("LABEL", c.Closest("label")?.TagName));
    }

    [Fact]
    public void Operator_DoesNotSeeOnlyMine_AndSeesOnlyOwn()
    {
        Seed(2, createdBy: OperatorId);
        using (var db = CreateDb())
        {
            db.Diagnostics.Add(new Diagnostic { Id = Guid.NewGuid(), TransactionId = 3000, CreatedByUserId = AuditorId, Markdown = Markdown, CreatedAt = DateTime.UtcNow });
            db.SaveChanges();
        }

        var cut = Render("Operador");
        Assert.DoesNotContain("Solo los míos", cut.Find(".hist-filters").TextContent);
        Assert.Equal(2, cut.FindAll("tbody tr").Count);
    }

    [Fact]
    public void NoResults_ShowsMessageAndClear_AndHidesThePanel()
    {
        Seed(2);
        var cut = Render();

        cut.Find("input.hist-input-card").Input("9999");

        cut.WaitForAssertion(() => Assert.Contains("No hay diagnósticos con estos filtros", cut.Find(".hist-empty").TextContent), Wait);
        Assert.Empty(cut.FindAll(".hist-panel"));
        Assert.Empty(cut.FindAll("table"));

        cut.Find(".hist-empty button").Click();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count), Wait);
        Assert.Single(cut.FindAll(".hist-panel"));
        Assert.Equal(string.Empty, cut.Find("input.hist-input-card").GetAttribute("value") ?? string.Empty);
    }

    [Fact]
    public void ExportPdf_ShowsNotice_WhenThePopupIsBlocked()
    {
        Seed(1);
        _ctx.JSInterop.Setup<bool>("smartOpsOpenPrintView", _ => true).SetResult(false);
        var cut = Render();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".hist-panel .diag-block")), Wait);

        cut.FindAll(".hist-export-btn").Single(b => b.TextContent.Contains("Descargar PDF")).Click();

        cut.WaitForAssertion(() => Assert.Contains("bloqueó la ventana", cut.Find(".hist-export-notice").TextContent), Wait);
        var html = _ctx.JSInterop.Invocations["smartOpsOpenPrintView"].Single().Arguments[0] as string;
        Assert.Contains("Transacción 2000 · Expired card", html);
    }

    private sealed class RoleAuthProvider(string role, string userId) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, role == "Auditor" ? "admin@smartops.com" : "operador@smartops.com"),
                new Claim(ClaimTypes.Role, role)
            }, "Test");
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }
}
