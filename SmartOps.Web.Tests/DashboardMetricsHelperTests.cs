using System;
using System.Collections.Generic;
using System.Linq;
using SmartOps.Core.Entities;
using SmartOps.Web.Services;
using Xunit;

namespace SmartOps.Web.Tests;

public sealed class DashboardMetricsHelperTests
{
    private static Transaction Tx(int id, string status, string? error = null) =>
        new() { Id = id, Status = status, ErrorMessage = error };

    [Fact]
    public void SuccessRate_CountsSimulatorSucceededStatusAsApproved()
    {
        // Mismo vocabulario que SimulatorTransactionService: "Succeeded" / "Failed".
        var txs = Enumerable.Range(1, 10)
            .Select(i => Tx(i, i <= 4 ? "Succeeded" : "Failed", i <= 4 ? null : "Card declined"))
            .ToList();

        var metrics = DashboardMetricsHelper.Build(txs, null, null);

        Assert.Equal(4, metrics.ApprovedCount);
        Assert.Equal(6, metrics.FailedCount);
        Assert.Equal(40, metrics.SuccessRatePercent);
    }

    [Fact]
    public void SuccessRate_AcceptsAllApprovedAliases()
    {
        var txs = new[]
        {
            Tx(1, "Succeeded"), Tx(2, "Approved"), Tx(3, "success"),
            Tx(4, "Failed", "Expired card")
        };

        var metrics = DashboardMetricsHelper.Build(txs, null, null);

        Assert.Equal(3, metrics.ApprovedCount);
        Assert.Equal(75, metrics.SuccessRatePercent);
    }

    [Fact]
    public void SuccessRate_IsZeroWithoutTransactions()
    {
        var metrics = DashboardMetricsHelper.Build(Array.Empty<Transaction>(), null, null);
        Assert.Equal(0, metrics.SuccessRatePercent);
        Assert.Empty(metrics.FailureReasons);
    }

    [Fact]
    public void FailureReasons_Top5_WithPercentOfFailedAndRelativeBar()
    {
        var txs = new List<Transaction>();
        var id = 1;
        void Add(string reason, int n) { for (var i = 0; i < n; i++) txs.Add(Tx(id++, "Failed", reason)); }
        Add("Suspected fraud", 8);
        Add("Card declined", 6);
        Add("Expired card", 3);
        Add("Insufficient funds", 1);
        Add("Stolen card", 1);
        Add("Do not honor", 1);
        txs.Add(Tx(id++, "Succeeded"));
        txs.Add(Tx(id++, "Succeeded"));

        var reasons = DashboardMetricsHelper.Build(txs, null, null).FailureReasons;

        Assert.Equal(5, reasons.Count);
        Assert.Equal(new[] { "Suspected fraud", "Card declined", "Expired card" }, reasons.Take(3).Select(r => r.Reason));
        // Porcentaje sobre las 20 fallidas, no sobre el total.
        Assert.Equal(40, reasons[0].Percent);
        Assert.Equal(30, reasons[1].Percent);
        Assert.Equal(15, reasons[2].Percent);
        Assert.Equal(5, reasons[4].Percent);
        // Barra relativa al motivo más frecuente.
        Assert.Equal(100, reasons[0].BarPercent);
        Assert.Equal(75, reasons[1].BarPercent);
        Assert.Equal(12.5, reasons[4].BarPercent);
    }

    [Fact]
    public void FailureReasons_GroupsMissingMessage()
    {
        var txs = new[] { Tx(1, "Failed"), Tx(2, "Failed", "  ") };
        var reasons = DashboardMetricsHelper.Build(txs, null, null).FailureReasons;
        Assert.Single(reasons);
        Assert.Equal(DashboardMetricsHelper.UnknownReason, reasons[0].Reason);
    }

    [Fact]
    public void Queue_PendingAreFailedWithoutDiagnostic()
    {
        var txs = new[]
        {
            Tx(1, "Failed", "A"), Tx(2, "Failed", "A"), Tx(3, "Failed", "B"), Tx(4, "Failed", "B"),
            Tx(5, "Succeeded")
        };
        var diags = new[]
        {
            new Diagnostic { Id = Guid.NewGuid(), TransactionId = 1 },
            new Diagnostic { Id = Guid.NewGuid(), TransactionId = 1 }, // re-análisis: no cuenta doble
            new Diagnostic { Id = Guid.NewGuid(), TransactionId = 3 },
            new Diagnostic { Id = Guid.NewGuid(), TransactionId = 99 } // transacción fuera del set
        };

        var metrics = DashboardMetricsHelper.Build(txs, null, diags);

        Assert.Equal(2, metrics.DiagnosedFailedCount);
        Assert.Equal(2, metrics.PendingFailedCount);
        Assert.Equal(4, metrics.DiagnosticsCount);
    }

    [Fact]
    public void UsefulPercent_FromVotes_OrMinusOneWhenNone()
    {
        var votes = new[]
        {
            new DiagnosticFeedback { IsUseful = true },
            new DiagnosticFeedback { IsUseful = true },
            new DiagnosticFeedback { IsUseful = false }
        };

        Assert.Equal(67, DashboardMetricsHelper.Build(null, votes, null).UsefulPercent);
        Assert.Equal(-1, DashboardMetricsHelper.Build(null, null, null).UsefulPercent);
    }
}
