using System;
using SmartOps.Core.Entities;
using SmartOps.Web.Services;
using Xunit;

namespace SmartOps.Web.Tests;

public sealed class DiagnosticsHistoryFilterHelperTests
{
    [Fact]
    public void OperatorFilter_IgnoresOtherUsers()
    {
        var diag = new Diagnostic
        {
            CreatedAt = new DateTime(2026, 7, 30),
            CardLast4 = "4532",
            CreatedByUserId = "admin-id"
        };

        var matches = DiagnosticsHistoryFilterHelper.Matches(diag, "", null, null, true, "operator-id", false);

        Assert.False(matches);
    }

    [Fact]
    public void AdminOnlyMineFilter_RespectsCurrentUser()
    {
        var diag = new Diagnostic
        {
            CreatedAt = new DateTime(2026, 7, 30),
            CardLast4 = "4532",
            CreatedByUserId = "admin-id"
        };

        var matchesMine = DiagnosticsHistoryFilterHelper.Matches(diag, "", null, null, true, "admin-id", true);
        var matchesAll = DiagnosticsHistoryFilterHelper.Matches(diag, "", null, null, false, "admin-id", true);

        Assert.True(matchesMine);
        Assert.True(matchesAll);
    }

    [Fact]
    public void DateRangeAndTextFilters_Combine()
    {
        var diag = new Diagnostic
        {
            CreatedAt = new DateTime(2026, 7, 30),
            CardLast4 = "4532",
            CreatedByUserId = "admin-id"
        };

        var matches = DiagnosticsHistoryFilterHelper.Matches(
            diag,
            "4532",
            new DateTime(2026, 7, 29),
            new DateTime(2026, 7, 30),
            false,
            "admin-id",
            true);

        Assert.True(matches);
    }
}
