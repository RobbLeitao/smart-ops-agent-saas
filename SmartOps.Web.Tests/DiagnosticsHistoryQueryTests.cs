using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartOps.Core.Entities;
using SmartOps.Infrastructure.Data;
using SmartOps.Web.Services;
using Xunit;

namespace SmartOps.Web.Tests;

/// <summary>
/// Consulta paginada del Historial sobre SQLite en memoria (la misma traducción SQL que en producción).
/// </summary>
public sealed class DiagnosticsHistoryQueryTests : IDisposable
{
    private const string Auditor = "auditor-id";
    private const string Operator = "operator-id";

    // Zona fija (UTC-3, sin horario de verano) para que las fechas no dependan de la máquina.
    private static readonly TimeZoneInfo Tz = TimeZoneInfo.CreateCustomTimeZone("Test-03", TimeSpan.FromHours(-3), "Test-03", "Test-03");

    private readonly SqliteConnection _connection;

    public DiagnosticsHistoryQueryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
        db.Users.AddRange(
            new ApplicationUser { Id = Auditor, UserName = "admin@smartops.com", Email = "admin@smartops.com" },
            new ApplicationUser { Id = Operator, UserName = "operador@smartops.com", Email = "operador@smartops.com" });
        db.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    private static Diagnostic Diag(int tx, DateTime createdUtc, string? card = "4242", string? by = Operator) => new()
    {
        Id = Guid.NewGuid(), TransactionId = tx, CreatedAt = createdUtc, CardLast4 = card,
        CreatedByUserId = by, Markdown = "## Resumen\nx"
    };

    private async Task<HistoryPage> Page(HistoryFilters f, string? user = Auditor, bool all = true, int page = 1)
    {
        await using var db = CreateDb();
        return await DiagnosticsHistoryQuery.GetPageAsync(db, f, user, all, page, timeZone: Tz);
    }

    [Fact]
    public async Task Paginates_20_PerPage_NewestFirst_AndClampsPage()
    {
        var start = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        await using (var db = CreateDb())
        {
            for (var i = 0; i < 45; i++) db.Diagnostics.Add(Diag(1000 + i, start.AddMinutes(i)));
            await db.SaveChangesAsync();
        }

        var p1 = await Page(HistoryFilters.None);
        Assert.Equal(45, p1.Total);
        Assert.Equal(20, p1.Rows.Count);
        Assert.Equal(1044, p1.Rows[0].TransactionId);
        Assert.Equal((1, 20), (p1.FirstIndex, p1.LastIndex));
        Assert.False(p1.HasPrevious);
        Assert.True(p1.HasNext);

        var p3 = await Page(HistoryFilters.None, page: 3);
        Assert.Equal(5, p3.Rows.Count);
        Assert.Equal((41, 45), (p3.FirstIndex, p3.LastIndex));
        Assert.True(p3.HasPrevious);
        Assert.False(p3.HasNext);
        Assert.Equal(1000, p3.Rows[^1].TransactionId);

        // Una página que ya no existe (p. ej. después de filtrar) cae en la última.
        var p9 = await Page(HistoryFilters.None, page: 9);
        Assert.Equal(3, p9.Page);
    }

    [Fact]
    public async Task Projects_Reason_OperatorEmail_AndCurrentUserVote()
    {
        var d = Diag(1500, DateTime.UtcNow);
        await using (var db = CreateDb())
        {
            db.Transactions.Add(new Transaction { Id = 1500, CustomerId = 1, Amount = 10, Currency = "USD", Status = "Failed", ErrorMessage = "Expired card", Provider = "Simulator" });
            db.Diagnostics.Add(d);
            db.DiagnosticFeedback.Add(new DiagnosticFeedback { Id = Guid.NewGuid(), DiagnosticId = d.Id, UserId = Auditor, IsUseful = true });
            db.DiagnosticFeedback.Add(new DiagnosticFeedback { Id = Guid.NewGuid(), DiagnosticId = d.Id, UserId = Operator, IsUseful = false });
            await db.SaveChangesAsync();
        }

        var row = Assert.Single((await Page(HistoryFilters.None)).Rows);
        Assert.Equal("Expired card", row.Reason);
        Assert.Equal("operador@smartops.com", row.OperatorEmail);
        Assert.True(row.UserVote);

        var asOperator = Assert.Single((await Page(HistoryFilters.None, Operator, all: false)).Rows);
        Assert.False(asOperator.UserVote);
    }

    [Fact]
    public async Task MissingTransactionOrCreator_StillReturnsRow()
    {
        await using (var db = CreateDb())
        {
            db.Diagnostics.Add(Diag(9999, DateTime.UtcNow, by: null));
            await db.SaveChangesAsync();
        }

        var row = Assert.Single((await Page(HistoryFilters.None)).Rows);
        Assert.Null(row.Reason);
        Assert.Null(row.OperatorEmail);
        Assert.Null(row.UserVote);
    }

    [Theory]
    [InlineData("42", 2)]
    [InlineData("4242", 1)]
    [InlineData("1", 1)]
    [InlineData("9", 0)]
    [InlineData("12345", 0)]
    [InlineData("4a", 0)]
    [InlineData("", 3)]
    public async Task CardFilter_IsContains_OnUpTo4Digits(string digits, int expected)
    {
        await using (var db = CreateDb())
        {
            db.Diagnostics.AddRange(Diag(1, DateTime.UtcNow, "4242"), Diag(2, DateTime.UtcNow, "1842"), Diag(3, DateTime.UtcNow, null));
            await db.SaveChangesAsync();
        }

        Assert.Equal(expected, (await Page(new HistoryFilters(CardDigits: digits))).Total);
    }

    [Fact]
    public async Task DateFilter_UsesLocalDays_NotUtcDates()
    {
        // 14/11 01:30 UTC = 13/11 22:30 en UTC-3: pertenece al día local 13/11.
        await using (var db = CreateDb())
        {
            db.Diagnostics.AddRange(
                Diag(1, new DateTime(2026, 11, 13, 2, 59, 0, DateTimeKind.Utc)),  // 12/11 23:59 local
                Diag(2, new DateTime(2026, 11, 13, 3, 0, 0, DateTimeKind.Utc)),   // 13/11 00:00 local
                Diag(3, new DateTime(2026, 11, 14, 1, 30, 0, DateTimeKind.Utc)),  // 13/11 22:30 local
                Diag(4, new DateTime(2026, 11, 14, 3, 0, 0, DateTimeKind.Utc)));  // 14/11 00:00 local
            await db.SaveChangesAsync();
        }

        var day = new DateTime(2026, 11, 13);
        var only13 = await Page(new HistoryFilters(From: day, To: day));
        Assert.Equal(new[] { 3, 2 }, only13.Rows.Select(r => r.TransactionId));

        Assert.Equal(3, (await Page(new HistoryFilters(From: day))).Total);
        Assert.Equal(3, (await Page(new HistoryFilters(To: day))).Total);
    }

    [Fact]
    public async Task VoteFilter_UsesOnlyTheCurrentUsersVote()
    {
        var useful = Diag(1, DateTime.UtcNow);
        var notUseful = Diag(2, DateTime.UtcNow);
        var votedByOther = Diag(3, DateTime.UtcNow);
        var none = Diag(4, DateTime.UtcNow);
        await using (var db = CreateDb())
        {
            db.Diagnostics.AddRange(useful, notUseful, votedByOther, none);
            db.DiagnosticFeedback.AddRange(
                new DiagnosticFeedback { Id = Guid.NewGuid(), DiagnosticId = useful.Id, UserId = Auditor, IsUseful = true },
                new DiagnosticFeedback { Id = Guid.NewGuid(), DiagnosticId = notUseful.Id, UserId = Auditor, IsUseful = false },
                new DiagnosticFeedback { Id = Guid.NewGuid(), DiagnosticId = votedByOther.Id, UserId = Operator, IsUseful = true });
            await db.SaveChangesAsync();
        }

        async Task<int[]> Txs(VoteFilter v) =>
            (await Page(new HistoryFilters(Vote: v))).Rows.Select(r => r.TransactionId).OrderBy(x => x).ToArray();

        Assert.Equal(new[] { 1 }, await Txs(VoteFilter.Useful));
        Assert.Equal(new[] { 2 }, await Txs(VoteFilter.NotUseful));
        Assert.Equal(new[] { 3, 4 }, await Txs(VoteFilter.Unvoted));
        Assert.Equal(new[] { 1, 2, 3, 4 }, await Txs(VoteFilter.All));
    }

    [Fact]
    public async Task OnlyMine_ForAuditor_AndOperatorAlwaysSeesOnlyOwn()
    {
        await using (var db = CreateDb())
        {
            db.Diagnostics.AddRange(Diag(1, DateTime.UtcNow, by: Auditor), Diag(2, DateTime.UtcNow, by: Operator), Diag(3, DateTime.UtcNow, by: Operator));
            await db.SaveChangesAsync();
        }

        Assert.Equal(3, (await Page(HistoryFilters.None, Auditor, all: true)).Total);
        Assert.Equal(1, (await Page(new HistoryFilters(OnlyMine: true), Auditor, all: true)).Total);

        // El Operador ve solo los suyos aunque no marque «Solo los míos».
        Assert.Equal(2, (await Page(HistoryFilters.None, Operator, all: false)).Total);
        // Sin usuario no se ve nada.
        Assert.Equal(0, (await Page(HistoryFilters.None, null, all: false)).Total);
    }

    [Fact]
    public async Task Filters_Combine_AndTotalMatchesFilteredCount()
    {
        var start = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
        await using (var db = CreateDb())
        {
            for (var i = 0; i < 30; i++) db.Diagnostics.Add(Diag(100 + i, start.AddMinutes(i), i % 2 == 0 ? "4242" : "1111", i % 3 == 0 ? Auditor : Operator));
            await db.SaveChangesAsync();
        }

        var page = await Page(new HistoryFilters(CardDigits: "42", From: new DateTime(2026, 11, 13), OnlyMine: true));
        // Pares (tarjeta 4242) y múltiplos de 3 (Auditor): múltiplos de 6 entre 0 y 29 → 5.
        Assert.Equal(5, page.Total);
        Assert.All(page.Rows, r => Assert.Equal("4242", r.CardLast4));
    }

    [Fact]
    public void Selection_KeepsCurrentIfVisible_ElseFirst_ElseNull()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        Assert.Equal(a, HistorySelection.Resolve(null, new[] { a, b }));
        Assert.Equal(b, HistorySelection.Resolve(b, new[] { a, b }));
        Assert.Equal(a, HistorySelection.Resolve(c, new[] { a, b }));
        Assert.Null(HistorySelection.Resolve(a, Array.Empty<Guid>()));
    }

    [Theory]
    [InlineData("admin@smartops.com", "admin")]
    [InlineData("ana.perez@empresa.com.ar", "ana.perez")]
    [InlineData("sinarroba", "sinarroba")]
    [InlineData("@raro.com", "@raro.com")]
    [InlineData("", "—")]
    [InlineData(null, "—")]
    public void OperatorName_IsTheLocalPart(string? email, string expected) =>
        Assert.Equal(expected, OperatorName.Short(email));
}
