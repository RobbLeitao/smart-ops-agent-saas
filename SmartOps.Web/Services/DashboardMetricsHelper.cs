using SmartOps.Core.Entities;

namespace SmartOps.Web.Services;

public sealed record DashboardMetrics(
    int FailedTransactionsCount,
    int DiagnosticsCount,
    int ApprovedTransactionsCount,
    int SuccessRatePercent,
    int UsefulPercent,
    IReadOnlyList<(string Reason, int Count)> FailureReasons);

public static class DashboardMetricsHelper
{
    public static DashboardMetrics Build(
        IEnumerable<Transaction> transactions,
        IEnumerable<DiagnosticFeedback> feedback,
        IEnumerable<Diagnostic> diagnostics,
        bool onlyMyDiagnostics,
        string? currentUserId)
    {
        var txs = transactions ?? Enumerable.Empty<Transaction>();
        var diags = diagnostics ?? Enumerable.Empty<Diagnostic>();

        if (onlyMyDiagnostics && !string.IsNullOrWhiteSpace(currentUserId))
        {
            diags = diags.Where(d => d.CreatedByUserId == currentUserId);
        }

        var totalTx = txs.Count();
        var failedTx = txs.Count(t => string.Equals(t.Status, "Failed", StringComparison.OrdinalIgnoreCase));
        var approvedTx = txs.Count(t => string.Equals(t.Status, "Approved", StringComparison.OrdinalIgnoreCase) || string.Equals(t.Status, "Success", StringComparison.OrdinalIgnoreCase));
        var usefulCount = feedback.Count(f => f.IsUseful);
        var totalRated = feedback.Count();
        var usefulPercent = totalRated == 0 ? -1 : (int)Math.Round((double)usefulCount / totalRated * 100);
        var successRate = totalTx == 0 ? 0 : (int)Math.Round((double)approvedTx / totalTx * 100);

        var reasons = txs
            .Where(t => string.Equals(t.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            .GroupBy(t => t.ErrorMessage ?? "Sin error")
            .Select(g => (g.Key, g.Count()))
            .OrderByDescending(x => x.Item2)
            .Take(5)
            .ToList();

        return new DashboardMetrics(
            failedTx,
            diags.Count(),
            approvedTx,
            successRate,
            usefulPercent,
            reasons);
    }
}
