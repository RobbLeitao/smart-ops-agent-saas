using System;
using System.Collections.Generic;
using SmartOps.Core.Entities;
using SmartOps.Web.Services;
using Xunit;

namespace SmartOps.Web.Tests;

public sealed class DashboardMetricsHelperTests
{
    [Fact]
    public void ToggleDoesNotChangeFailureMetrics()
    {
        var txs = new[]
        {
            new Transaction { Id = 1, Status = "Failed", ErrorMessage = "Card declined" },
            new Transaction { Id = 2, Status = "Approved", ErrorMessage = null },
            new Transaction { Id = 3, Status = "Approved", ErrorMessage = null }
        };

        var diagnostics = new[]
        {
            new Diagnostic { CreatedByUserId = "admin", TransactionId = 1 },
            new Diagnostic { CreatedByUserId = "other", TransactionId = 2 },
            new Diagnostic { CreatedByUserId = "admin", TransactionId = 3 }
        };

        var feedback = Array.Empty<DiagnosticFeedback>();
        var metrics = DashboardMetricsHelper.Build(txs, feedback, diagnostics, true, "admin");

        Assert.Equal(3, metrics.DiagnosticsCount);
        Assert.Equal(1, metrics.FailedTransactionsCount);
        Assert.Equal(2, metrics.ApprovedTransactionsCount);
    }

    [Fact]
    public void FailureReasons_AreGroupedAndSorted()
    {
        var txs = new[]
        {
            new Transaction { Id = 1, Status = "Failed", ErrorMessage = "Card declined" },
            new Transaction { Id = 2, Status = "Failed", ErrorMessage = "Card declined" },
            new Transaction { Id = 3, Status = "Failed", ErrorMessage = "Expired card" },
            new Transaction { Id = 4, Status = "Approved", ErrorMessage = null }
        };

        var metrics = DashboardMetricsHelper.Build(txs, Array.Empty<DiagnosticFeedback>(), Array.Empty<Diagnostic>(), false, null);

        Assert.Equal(("Card declined", 2), metrics.FailureReasons[0]);
        Assert.Equal(("Expired card", 1), metrics.FailureReasons[1]);
        Assert.Equal(25, metrics.SuccessRatePercent);
    }
}
