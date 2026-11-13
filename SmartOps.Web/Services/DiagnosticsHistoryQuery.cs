using Microsoft.EntityFrameworkCore;
using SmartOps.Core.Entities;
using SmartOps.Infrastructure.Data;

namespace SmartOps.Web.Services;

/// <summary>Filtro «Valoración» del Historial: siempre sobre el voto del usuario actual.</summary>
public enum VoteFilter { All, Useful, NotUseful, Unvoted }

/// <summary>Filtros del Historial. Las fechas son días locales (los del input date).</summary>
public sealed record HistoryFilters(
    string? CardDigits = null,
    DateTime? From = null,
    DateTime? To = null,
    VoteFilter Vote = VoteFilter.All,
    bool OnlyMine = false)
{
    public static HistoryFilters None { get; } = new();

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(CardDigits) && From is null && To is null && Vote == VoteFilter.All && !OnlyMine;
}

/// <summary>Una fila del Historial. No trae el Markdown: el panel lo carga solo para el seleccionado.</summary>
public sealed record HistoryRow(
    Guid Id,
    DateTime CreatedAtUtc,
    int TransactionId,
    string? CardLast4,
    string? Reason,
    string? OperatorEmail,
    bool? UserVote);

/// <summary>Página de resultados. <see cref="Page"/> ya viene acotada a las páginas que existen.</summary>
public sealed record HistoryPage(IReadOnlyList<HistoryRow> Rows, int Total, int Page, int PageSize)
{
    public int FirstIndex => Total == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int LastIndex => Total == 0 ? 0 : FirstIndex + Rows.Count - 1;
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page * PageSize < Total;
}

/// <summary>
/// Consulta paginada del Historial, con todos los filtros aplicados en la base (SQLite): una consulta
/// para el total y otra para la página, que ya trae el motivo (Transactions), el email del operador
/// (AspNetUsers) y el voto del usuario actual (DiagnosticFeedback).
/// </summary>
public static class DiagnosticsHistoryQuery
{
    public const int DefaultPageSize = 20;

    /// <param name="canSeeAll">Auditor/Admin: ve todos los diagnósticos. El Operador solo los suyos.</param>
    /// <param name="timeZone">Zona de las fechas del filtro. Por defecto, la del servidor (la misma con que se muestran).</param>
    public static async Task<HistoryPage> GetPageAsync(
        AppDbContext db,
        HistoryFilters filters,
        string? userId,
        bool canSeeAll,
        int page,
        int pageSize = DefaultPageSize,
        TimeZoneInfo? timeZone = null,
        CancellationToken ct = default)
    {
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize));

        var query = Filter(db, filters ?? HistoryFilters.None, userId, canSeeAll, timeZone ?? TimeZoneInfo.Local);

        var total = await query.CountAsync(ct);
        var lastPage = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, lastPage);

        if (total == 0)
        {
            return new HistoryPage(Array.Empty<HistoryRow>(), 0, 1, pageSize);
        }

        var rows = await (
                from d in query
                join t in db.Transactions on d.TransactionId equals t.Id into tj
                from t in tj.DefaultIfEmpty()
                join u in db.Users on d.CreatedByUserId equals u.Id into uj
                from u in uj.DefaultIfEmpty()
                orderby d.CreatedAt descending, d.Id descending
                select new HistoryRow(
                    d.Id,
                    d.CreatedAt,
                    d.TransactionId,
                    d.CardLast4,
                    t == null ? null : t.ErrorMessage,
                    u == null ? null : u.Email,
                    db.DiagnosticFeedback
                        .Where(f => f.DiagnosticId == d.Id && f.UserId == userId)
                        .Select(f => (bool?)f.IsUseful)
                        .FirstOrDefault()))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);

        return new HistoryPage(rows, total, page, pageSize);
    }

    internal static IQueryable<Diagnostic> Filter(AppDbContext db, HistoryFilters filters, string? userId, bool canSeeAll, TimeZoneInfo timeZone)
    {
        var query = db.Diagnostics.AsNoTracking();

        // El Operador ve solo los suyos; sin usuario no ve nada. El Auditor puede pedir «Solo los míos».
        if (!canSeeAll || filters.OnlyMine)
        {
            query = string.IsNullOrWhiteSpace(userId)
                ? query.Where(_ => false)
                : query.Where(d => d.CreatedByUserId == userId);
        }

        // Tarjeta: de 1 a 4 dígitos, «contiene» (igual que el Dashboard). Otra cosa no matchea nada.
        var digits = filters.CardDigits?.Trim();
        if (!string.IsNullOrEmpty(digits))
        {
            query = digits.Length <= 4 && digits.All(char.IsAsciiDigit)
                ? query.Where(d => d.CardLast4 != null && d.CardLast4.Contains(digits))
                : query.Where(_ => false);
        }

        // Fechas: los días son locales; CreatedAt se guarda en UTC. [desde 00:00 local, hasta+1 00:00 local).
        if (filters.From is { } from)
        {
            var fromUtc = LocalDayStartUtc(from, timeZone);
            query = query.Where(d => d.CreatedAt >= fromUtc);
        }

        if (filters.To is { } to)
        {
            var toUtcExclusive = LocalDayStartUtc(to.Date.AddDays(1), timeZone);
            query = query.Where(d => d.CreatedAt < toUtcExclusive);
        }

        switch (filters.Vote)
        {
            case VoteFilter.Useful:
                query = query.Where(d => db.DiagnosticFeedback.Any(f => f.DiagnosticId == d.Id && f.UserId == userId && f.IsUseful));
                break;
            case VoteFilter.NotUseful:
                query = query.Where(d => db.DiagnosticFeedback.Any(f => f.DiagnosticId == d.Id && f.UserId == userId && !f.IsUseful));
                break;
            case VoteFilter.Unvoted:
                query = query.Where(d => !db.DiagnosticFeedback.Any(f => f.DiagnosticId == d.Id && f.UserId == userId));
                break;
        }

        return query;
    }

    internal static DateTime LocalDayStartUtc(DateTime day, TimeZoneInfo timeZone)
    {
        var localMidnight = DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified);
        // Un día que arranca dentro de un salto de horario de verano: se corre hasta la primera hora válida.
        while (timeZone.IsInvalidTime(localMidnight)) localMidnight = localMidnight.AddMinutes(30);
        return TimeZoneInfo.ConvertTimeToUtc(localMidnight, timeZone);
    }
}

/// <summary>Regla de selección del Historial.</summary>
public static class HistorySelection
{
    /// <summary>
    /// Mantiene el seleccionado si sigue visible; si no, el primero visible; null si no hay filas.
    /// </summary>
    public static Guid? Resolve(Guid? current, IReadOnlyList<Guid> visible)
    {
        if (visible.Count == 0) return null;
        return current is { } id && visible.Contains(id) ? id : visible[0];
    }
}

/// <summary>Presentación del operador en la tabla del Historial.</summary>
public static class OperatorName
{
    /// <summary>La parte del email antes de la @ («admin@smartops.com» → «admin»). Sin email: «—».</summary>
    public static string Short(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "—";
        var at = email.IndexOf('@');
        return at > 0 ? email[..at] : email.Trim();
    }
}
