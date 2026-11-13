using System.Globalization;

namespace SmartOps.Web.Services;

/// <summary>Diagnóstico abierto en el panel del Historial. <see cref="Markdown"/> es null mientras se carga.</summary>
public sealed record HistoryDetail(
    Guid Id,
    int TransactionId,
    DateTime CreatedAtUtc,
    string? OperatorEmail,
    string? CardLast4,
    string? Reason,
    string? Markdown)
{
    public static HistoryDetail FromRow(HistoryRow row, string? markdown = null) =>
        new(row.Id, row.TransactionId, row.CreatedAtUtc, row.OperatorEmail, row.CardLast4, row.Reason, markdown);
}

/// <summary>Formatos de la tabla y el panel del Historial.</summary>
public static class HistoryFormat
{
    /// <summary>SQLite devuelve Unspecified: las fechas se guardan en UTC.</summary>
    public static DateTime ToLocal(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime();

    /// <summary>«13/11 13:56», la fecha corta de la tabla.</summary>
    public static string ShortDate(DateTime utc) => ToLocal(utc).ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);

    public static string Card(string? last4) => string.IsNullOrWhiteSpace(last4) ? "—" : $"•••• {last4}";

    public static string Reason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? DashboardMetricsHelper.UnknownReason : reason.Trim();
}
