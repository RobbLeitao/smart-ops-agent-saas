using SmartOps.Core.Entities;

namespace SmartOps.Web.Services;

public static class DiagnosticsHistoryFilterHelper
{
    public static bool Matches(
        Diagnostic diagnostic,
        string? last4,
        DateTime? from,
        DateTime? to,
        bool onlyMine,
        string? currentUserId,
        bool isAdmin)
    {
        if (diagnostic == null) return false;

        if (onlyMine && !string.IsNullOrWhiteSpace(currentUserId) && diagnostic.CreatedByUserId != currentUserId)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(last4))
        {
            if (last4.Length > 4 || !last4.All(char.IsDigit)) return false;
            if (!((diagnostic.CardLast4 ?? string.Empty).Contains(last4))) return false;
        }

        var created = diagnostic.CreatedAt.Date;
        if (from.HasValue && created < from.Value.Date) return false;
        if (to.HasValue && created > to.Value.Date) return false;

        return true;
    }
}
