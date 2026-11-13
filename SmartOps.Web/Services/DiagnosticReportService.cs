using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartOps.Core.Entities;
using SmartOps.Infrastructure.Data;
using SmartOps.Web.Components.Report;

namespace SmartOps.Web.Services;

/// <summary>Datos ya formateados del informe PDF (<see cref="DiagnosticReport"/>).</summary>
public sealed record DiagnosticReportModel(
    Guid DiagnosticId,
    int TransactionId,
    string Reason,
    string Amount,
    string Card,
    string Date,
    string Channel,
    string Operator,
    string Vote,
    string GeneratedDate,
    string GeneratedTime,
    string Markdown,
    string ExportedAt,
    string ExportedBy,
    string BaseHref);

/// <summary>
/// Arma el informe PDF de un diagnóstico del lado del servidor: junta los datos (diagnóstico, transacción,
/// operador y el voto de quien exporta) y renderiza <see cref="DiagnosticReport"/> a HTML con
/// <see cref="HtmlRenderer"/>. El navegador solo lo abre e imprime (wwwroot/js/export.js).
/// </summary>
public sealed class DiagnosticReportService
{
    public const string Unvoted = "Sin votar";

    private readonly AppDbContext _db;
    private readonly DbContextGate _gate;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _timeZone;

    public DiagnosticReportService(
        AppDbContext db,
        DbContextGate gate,
        IServiceScopeFactory scopes,
        ILoggerFactory loggerFactory,
        TimeProvider? time = null,
        TimeZoneInfo? timeZone = null)
    {
        _db = db;
        _gate = gate;
        _scopes = scopes;
        _loggerFactory = loggerFactory;
        _time = time ?? TimeProvider.System;
        _timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    /// <summary>HTML completo del informe, o null si el diagnóstico no existe.</summary>
    /// <param name="exportingUserId">Quien exporta: su voto es la «Valoración» del informe.</param>
    /// <param name="exportingUserEmail">Se muestra en el pie («Exportado … por …»).</param>
    /// <param name="baseHref">Base para resolver el logo desde la ventana de impresión (about:blank).</param>
    public async Task<string?> BuildHtmlAsync(Guid diagnosticId, string? exportingUserId, string? exportingUserEmail, string baseHref)
    {
        var model = await BuildModelAsync(diagnosticId, exportingUserId, exportingUserEmail, baseHref);
        return model is null ? null : await RenderAsync(model);
    }

    public Task<DiagnosticReportModel?> BuildModelAsync(Guid diagnosticId, string? exportingUserId, string? exportingUserEmail, string baseHref) =>
        _gate.RunAsync(async () =>
        {
            var diagnostic = await _db.Diagnostics.AsNoTracking().FirstOrDefaultAsync(d => d.Id == diagnosticId);
            if (diagnostic is null) return null;

            var tx = await _db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == diagnostic.TransactionId);
            var operatorEmail = string.IsNullOrWhiteSpace(diagnostic.CreatedByUserId)
                ? null
                : await _db.Users.AsNoTracking().Where(u => u.Id == diagnostic.CreatedByUserId).Select(u => u.Email).FirstOrDefaultAsync();
            bool? vote = string.IsNullOrWhiteSpace(exportingUserId)
                ? null
                : await _db.DiagnosticFeedback.AsNoTracking()
                    .Where(f => f.DiagnosticId == diagnosticId && f.UserId == exportingUserId)
                    .Select(f => (bool?)f.IsUseful)
                    .FirstOrDefaultAsync();

            var created = ToLocal(diagnostic.CreatedAt);
            var txDate = tx?.OccurredAt is { } occurred ? ToLocal(occurred) : created;
            var card = string.IsNullOrWhiteSpace(diagnostic.CardLast4) ? tx?.CardLast4 : diagnostic.CardLast4;
            var now = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _timeZone).DateTime;

            return (DiagnosticReportModel?)new DiagnosticReportModel(
                diagnostic.Id,
                diagnostic.TransactionId,
                Reason: string.IsNullOrWhiteSpace(tx?.ErrorMessage) ? DashboardMetricsHelper.UnknownReason : tx!.ErrorMessage!.Trim(),
                Amount: tx is null ? "—" : MoneyFormat.Format(tx.Amount, tx.Currency),
                Card: string.IsNullOrWhiteSpace(card) ? "—" : $"•••• {card}",
                Date: txDate.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                Channel: string.IsNullOrWhiteSpace(tx?.Provider) ? "—" : tx!.Provider,
                Operator: string.IsNullOrWhiteSpace(operatorEmail) ? "—" : operatorEmail!,
                Vote: VoteLabel(vote),
                GeneratedDate: created.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                GeneratedTime: created.ToString("HH:mm", CultureInfo.InvariantCulture),
                Markdown: diagnostic.Markdown ?? string.Empty,
                ExportedAt: now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                ExportedBy: string.IsNullOrWhiteSpace(exportingUserEmail) ? "—" : exportingUserEmail!,
                BaseHref: baseHref);
        });

    public async Task<string> RenderAsync(DiagnosticReportModel model)
    {
        // Scope propio: el informe no necesita nada del circuito y HtmlRenderer resuelve servicios scoped.
        await using var scope = _scopes.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, _loggerFactory);
        var body = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<DiagnosticReport>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(DiagnosticReport.Model)] = model }));
            return output.ToHtmlString();
        });
        return "<!doctype html>\n" + body;
    }

    /// <summary>«Útil», «No útil» o «Sin votar», igual que la columna Valoración del Historial.</summary>
    public static string VoteLabel(bool? vote) => vote switch
    {
        true => "Útil",
        false => "No útil",
        _ => Unvoted
    };

    // SQLite devuelve Unspecified: CreatedAt/OccurredAt se guardan en UTC.
    private DateTime ToLocal(DateTime value) => value.Kind == DateTimeKind.Local
        ? value
        : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(value, DateTimeKind.Utc), _timeZone);
}

/// <summary>
/// Exportación JSON del Historial. El contenido es exactamente el de antes del rediseño (mismo payload,
/// mismo nombre de archivo); solo se juntó en un lugar.
/// </summary>
public static class DiagnosticJsonExport
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static (string FileName, string Json) Build(Diagnostic diagnostic)
    {
        var payload = new
        {
            id = diagnostic.Id,
            createdAt = diagnostic.CreatedAt,
            transactionId = diagnostic.TransactionId,
            cardLast4 = diagnostic.CardLast4,
            markdown = diagnostic.Markdown,
            summary = diagnostic.Summary,
            suggestedMessage = GetSuggestedMessage(diagnostic.Markdown)
        };

        return ($"diagnostico-{diagnostic.TransactionId}.json", JsonSerializer.Serialize(payload, Options));
    }

    internal static string GetSuggestedMessage(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return "-";
        }

        var marker = "## 📨 Mensaje sugerido para el cliente";
        var index = markdown.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return "-";
        }

        var content = markdown.Substring(index + marker.Length).Trim();
        var nextHeading = content.IndexOf("## ", StringComparison.OrdinalIgnoreCase);
        if (nextHeading >= 0)
        {
            content = content.Substring(0, nextHeading).Trim();
        }

        return string.IsNullOrWhiteSpace(content) ? "-" : content;
    }
}
