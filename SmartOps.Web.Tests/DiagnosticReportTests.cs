using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartOps.Core.Entities;
using SmartOps.Infrastructure.Data;
using SmartOps.Web.Services;
using Xunit;

namespace SmartOps.Web.Tests;

/// <summary>HTML del informe PDF (DiagnosticReportService + DiagnosticReport.razor). Sin red.</summary>
public sealed class DiagnosticReportTests : IDisposable
{
    private const string LlmMarkdown =
        "```markdown\n" +
        "## 🔍 Resumen del Error\nEl emisor **rechazó** la tarjeta. <script>alert('x')</script>\n\n" +
        "## 🛠️ Acciones Recomendadas para el Operador\n- Verificar con el banco.\n- Ofrecer <img src=x onerror=alert(1)> otra tarjeta.\n\n" +
        "## 📨 Mensaje sugerido para el cliente\nHola, tu pago fue rechazado por tu banco.\n" +
        "```";

    private static readonly TimeZoneInfo Tz = TimeZoneInfo.CreateCustomTimeZone("Test-03", TimeSpan.FromHours(-3), "Test-03", "Test-03");

    private readonly SqliteConnection _connection;
    private readonly Guid _diagId = Guid.Parse("59d04994-9743-4d56-8da2-21b76eacb65d");

    public DiagnosticReportTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
        db.Users.Add(new ApplicationUser { Id = "op", UserName = "operador@smartops.com", Email = "operador@smartops.com" });
        db.Transactions.Add(new Transaction
        {
            Id = 1376, CustomerId = 1, Amount = 98.54m, Currency = "USD", Status = "Failed", CardLast4 = "5887",
            ErrorMessage = "Card <b>declined</b>", Provider = "Simulator", OccurredAt = new DateTime(2026, 11, 13, 16, 30, 0, DateTimeKind.Utc)
        });
        db.Diagnostics.Add(new Diagnostic
        {
            Id = _diagId, TransactionId = 1376, CardLast4 = "5887", CreatedByUserId = "op", Markdown = LlmMarkdown,
            CreatedAt = new DateTime(2026, 11, 13, 16, 32, 0, DateTimeKind.Utc)
        });
        db.DiagnosticFeedback.Add(new DiagnosticFeedback { Id = Guid.NewGuid(), DiagnosticId = _diagId, UserId = "auditor", IsUseful = true });
        db.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    private async Task<string> RenderAsync(string? userId = "auditor")
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var db = CreateDb();
        var time = new FixedTime(new DateTimeOffset(2026, 11, 13, 17, 5, 0, TimeSpan.Zero));
        var service = new DiagnosticReportService(db, new DbContextGate(), services.GetRequiredService<IServiceScopeFactory>(), NullLoggerFactory.Instance, time, Tz);
        var html = await service.BuildHtmlAsync(_diagId, userId, "admin@smartops.com", "https://smartops.test/");
        Assert.NotNull(html);
        return html!;
    }

    [Fact]
    public async Task Has_Header_Data_ThreeSections_AndFooter()
    {
        var html = await RenderAsync();

        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("<base href=\"https://smartops.test/\"", html);
        Assert.Contains("src=\"images/logo.png\"", html);
        Assert.Contains("Transacción 1376 · Card &lt;b&gt;declined&lt;/b&gt;", html);
        Assert.Contains("Diagnóstico generado por IA el 13/11/2026 a las 13:32", html);

        foreach (var label in new[] { "Monto", "Tarjeta", "Fecha", "Canal", "Operador", "Valoración" })
            Assert.Contains($"<dt>{label}</dt>", html);
        Assert.Contains("$ 98,54", html);
        // HtmlRenderer codifica lo no ASCII de los valores (•, Ú) como entidades: se compara el texto decodificado.
        var text = System.Net.WebUtility.HtmlDecode(html);
        Assert.Contains("•••• 5887", text);
        Assert.Contains("13/11/2026 13:30", html);
        Assert.Contains("Simulator", html);
        Assert.Contains("operador@smartops.com", html);
        Assert.Contains("<dt>Valoración</dt><dd>Útil</dd>", text);

        // Las 3 secciones, como títulos h2 del informe.
        var titles = Regex.Matches(html, "<h2 class=\"diag-overline\"[^>]*>([^<]+)</h2>").Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)).ToArray();
        Assert.Equal(new[] { "Qué pasó", "Qué hacer", "Mensaje para el cliente" }, titles);
        Assert.Contains("<strong>rechazó</strong>", html);
        Assert.Matches("<ol>\\s*<li>Verificar con el banco.</li>", html);
        Assert.Contains("class=\"diag-message diag-md\"", html);
        Assert.DoesNotContain("<button", html);

        Assert.Contains("ID de diagnóstico <span class=\"mono\">59d04994-9743-4d56-8da2-21b76eacb65d</span>", html);
        Assert.Contains("Documento interno de SmartOps. Los datos de tarjeta están enmascarados. Exportado el 13/11/2026 14:05 por admin@smartops.com.", html);
        Assert.DoesNotContain("Página", html);
    }

    [Fact]
    public async Task NeverContainsRawMarkdown()
    {
        var html = await RenderAsync();
        var body = html[html.IndexOf("<body>", StringComparison.Ordinal)..];

        Assert.DoesNotContain("```", body);
        Assert.DoesNotContain("## ", body);
        Assert.DoesNotContain("**", body);
        Assert.DoesNotContain("- Verificar", body);
        Assert.DoesNotContain("🔍", body);
    }

    [Fact]
    public async Task EscapesHtmlFromTheLlm()
    {
        var html = await RenderAsync();

        Assert.DoesNotContain("<script>alert", html);
        Assert.DoesNotContain("<img src=x", html);
        Assert.DoesNotContain("<b>declined", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public async Task Vote_IsTheExportersVote_OrSinVotar()
    {
        Assert.Contains("<dt>Valoración</dt><dd>Sin votar</dd>", await RenderAsync(userId: "otro"));
        Assert.Contains("<dt>Valoración</dt><dd>Sin votar</dd>", await RenderAsync(userId: null));
    }

    [Fact]
    public async Task PrintsBackgrounds_OnA4_WithoutBrowserMargins()
    {
        var html = await RenderAsync();

        Assert.Contains("@page { size: A4; margin: 0; }", html);
        Assert.Contains("print-color-adjust: exact;", html);
        Assert.Contains("-webkit-print-color-adjust: exact;", html);
        Assert.Contains("background: var(--print-band);", html);
        Assert.Contains("height: 88px;", html);
        Assert.Contains("background: var(--print-box);", html);
    }

    [Fact]
    public async Task PrintTokens_MatchTokensJson()
    {
        var html = await RenderAsync();
        var declared = Regex.Matches(html, "--(print-[a-z]+):\\s*(#[0-9a-fA-F]{6});")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.ToLowerInvariant());

        using var json = JsonDocument.Parse(File.ReadAllText(FindTokensJson()));
        var expected = json.RootElement.GetProperty("color").GetProperty("tokens").EnumerateArray()
            .Where(t => t.TryGetProperty("name", out var n) && n.GetString()!.StartsWith("print-"))
            .ToDictionary(t => t.GetProperty("name").GetString()!, t => t.GetProperty("value").GetString()!.ToLowerInvariant());

        Assert.NotEmpty(expected);
        Assert.Equal(expected.OrderBy(k => k.Key), declared.OrderBy(k => k.Key));

        // Fuera de la declaración de tokens, el CSS del informe no usa hex sueltos.
        var style = Regex.Match(html, "<style>(.*?)</style>", RegexOptions.Singleline).Groups[1].Value;
        var withoutRoot = Regex.Replace(style, ":root\\s*\\{.*?\\}", string.Empty, RegexOptions.Singleline);
        Assert.DoesNotMatch("#[0-9a-fA-F]{3,6}\\b", withoutRoot);
    }

    [Fact]
    public async Task UnknownDiagnostic_ReturnsNull()
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var db = CreateDb();
        var service = new DiagnosticReportService(db, new DbContextGate(), services.GetRequiredService<IServiceScopeFactory>(), NullLoggerFactory.Instance);
        Assert.Null(await service.BuildHtmlAsync(Guid.NewGuid(), "auditor", "a@b.c", "/"));
    }

    [Fact]
    public void JsonExport_KeepsThePreviousPayload()
    {
        var d = new Diagnostic
        {
            Id = _diagId, TransactionId = 1376, CardLast4 = "5887", Summary = "s",
            Markdown = "## Resumen\nx\n## 📨 Mensaje sugerido para el cliente\nHola.\n## Otro\ny",
            CreatedAt = new DateTime(2026, 11, 13, 16, 32, 0, DateTimeKind.Utc)
        };

        var (file, json) = DiagnosticJsonExport.Build(d);

        Assert.Equal("diagnostico-1376.json", file);
        using var doc = JsonDocument.Parse(json);
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "id", "createdAt", "transactionId", "cardLast4", "markdown", "summary", "suggestedMessage" }, names);
        Assert.Equal("Hola.", doc.RootElement.GetProperty("suggestedMessage").GetString());
        Assert.Contains("\n  \"id\"", json.Replace("\r\n", "\n"));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static string FindTokensJson()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "redesign", "tokens.json");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("docs/redesign/tokens.json");
    }
}
