using SmartOps.Core.Entities;

namespace SmartOps.Web.Services;

/// <summary>Un motivo de falla del panel «Motivos de falla».</summary>
/// <param name="Percent">Porcentaje sobre el total de fallidas (redondeado).</param>
/// <param name="BarPercent">Ancho de la barra relativo al motivo más frecuente (el primero vale 100).</param>
public sealed record FailureReasonStat(string Reason, int Count, int Percent, double BarPercent);

public sealed record DashboardMetrics(
    int TotalCount,
    int ApprovedCount,
    int FailedCount,
    int SuccessRatePercent,
    int DiagnosticsCount,
    int UsefulPercent,
    IReadOnlyList<FailureReasonStat> FailureReasons,
    int DiagnosedFailedCount,
    int PendingFailedCount);

public static class DashboardMetricsHelper
{
    public const int TopReasons = 5;
    public const string UnknownReason = "Sin motivo";

    public static DashboardMetrics Build(
        IEnumerable<Transaction>? transactions,
        IEnumerable<DiagnosticFeedback>? feedback,
        IEnumerable<Diagnostic>? diagnostics)
    {
        var txs = (transactions ?? Enumerable.Empty<Transaction>()).ToList();
        var votes = (feedback ?? Enumerable.Empty<DiagnosticFeedback>()).ToList();
        var diags = (diagnostics ?? Enumerable.Empty<Diagnostic>()).ToList();

        var failed = txs.Where(t => TransactionStatus.IsFailed(t.Status)).ToList();
        var approvedCount = txs.Count(t => TransactionStatus.IsApproved(t.Status));

        var diagnosedTxIds = diags.Select(d => d.TransactionId).ToHashSet();
        var diagnosedFailed = failed.Count(t => diagnosedTxIds.Contains(t.Id));

        var totalRated = votes.Count;
        var usefulPercent = totalRated == 0 ? -1 : Percent(votes.Count(f => f.IsUseful), totalRated);

        return new DashboardMetrics(
            TotalCount: txs.Count,
            ApprovedCount: approvedCount,
            FailedCount: failed.Count,
            SuccessRatePercent: SuccessRate(approvedCount, txs.Count),
            DiagnosticsCount: diags.Count,
            UsefulPercent: usefulPercent,
            FailureReasons: TopFailureReasons(failed),
            DiagnosedFailedCount: diagnosedFailed,
            PendingFailedCount: failed.Count - diagnosedFailed);
    }

    public static int SuccessRate(int approved, int total) => total == 0 ? 0 : Percent(approved, total);

    public static IReadOnlyList<FailureReasonStat> TopFailureReasons(IReadOnlyCollection<Transaction> failed)
    {
        if (failed.Count == 0) return Array.Empty<FailureReasonStat>();

        var groups = failed
            .GroupBy(t => string.IsNullOrWhiteSpace(t.ErrorMessage) ? UnknownReason : t.ErrorMessage!.Trim())
            .Select(g => (Reason: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Reason, StringComparer.OrdinalIgnoreCase)
            .Take(TopReasons)
            .ToList();

        var max = groups[0].Count;
        return groups
            .Select(g => new FailureReasonStat(g.Reason, g.Count, Percent(g.Count, failed.Count), Math.Round(g.Count * 100.0 / max, 1)))
            .ToList();
    }

    private static int Percent(int part, int total) => (int)Math.Round(part * 100.0 / total, MidpointRounding.AwayFromZero);
}
