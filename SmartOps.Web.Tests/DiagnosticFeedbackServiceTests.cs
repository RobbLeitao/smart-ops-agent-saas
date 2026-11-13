using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SmartOps.Core.Entities;
using SmartOps.Infrastructure.Data;
using SmartOps.Web.Services;
using Xunit;

namespace SmartOps.Web.Tests;

/// <summary>
/// SQLite en memoria (no InMemory): el proveedor InMemory no aplica índices únicos y el caso
/// concurrente depende de la violación real de IX_DiagnosticFeedback_DiagnosticId_UserId.
/// </summary>
public sealed class DiagnosticFeedbackServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Guid _diagnosticId = Guid.NewGuid();

    public DiagnosticFeedbackServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var db = CreateDb();
        db.Database.EnsureCreated();
        db.Diagnostics.Add(new Diagnostic { Id = _diagnosticId, TransactionId = 1, Markdown = "## Resumen\nx", CardLast4 = "4242" });
        db.SaveChanges();
    }

    private AppDbContext CreateDb(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptors)
            .Options;
        return new AppDbContext(options);
    }

    private static DiagnosticFeedbackService CreateService(AppDbContext db) =>
        new(db, NullLogger<DiagnosticFeedbackService>.Instance, new DbContextGate());

    [Fact]
    public async Task Vote_CreatesFeedback_AndKeepsLegacyWasHelpful()
    {
        await using (var db = CreateDb())
        {
            var feedback = await CreateService(db).VoteAsync(_diagnosticId, "user-1", isUseful: true);
            Assert.True(feedback.IsUseful);
        }

        await using var check = CreateDb();
        var row = Assert.Single(check.DiagnosticFeedback);
        Assert.Equal("user-1", row.UserId);
        Assert.True(row.IsUseful);
        Assert.True((await check.Diagnostics.SingleAsync(d => d.Id == _diagnosticId)).WasHelpful);
        Assert.True(await CreateService(check).GetUserVoteAsync(_diagnosticId, "user-1"));
        Assert.Null(await CreateService(check).GetUserVoteAsync(_diagnosticId, "otro"));
    }

    [Fact]
    public async Task Vote_ChangesExistingVote_WithoutDuplicating()
    {
        await using (var db = CreateDb())
        {
            var service = CreateService(db);
            await service.VoteAsync(_diagnosticId, "user-1", isUseful: true);
            await service.VoteAsync(_diagnosticId, "user-1", isUseful: false);
        }

        await using var check = CreateDb();
        var row = Assert.Single(check.DiagnosticFeedback);
        Assert.False(row.IsUseful);
        Assert.False((await check.Diagnostics.SingleAsync(d => d.Id == _diagnosticId)).WasHelpful);
    }

    [Fact]
    public async Task Vote_Concurrent_UniqueViolationIsRetriedAsUpdate()
    {
        // Simula otro request del mismo usuario que inserta su voto (Sí) justo después de que el servicio
        // leyó "no hay voto" y antes de que guarde el suyo (No).
        var competitor = new InsertCompetingVoteInterceptor(() => CreateDb(), _diagnosticId, "user-1", isUseful: true);

        DiagnosticFeedback result;
        await using (var db = CreateDb(competitor))
        {
            result = await CreateService(db).VoteAsync(_diagnosticId, "user-1", isUseful: false);
        }

        Assert.True(competitor.Fired);

        await using var check = CreateDb();
        var row = Assert.Single(check.DiagnosticFeedback);
        Assert.False(row.IsUseful); // ganó el UPDATE con el voto de este request
        Assert.Equal(row.Id, result.Id);
        Assert.False((await check.Diagnostics.SingleAsync(d => d.Id == _diagnosticId)).WasHelpful);
    }

    [Fact]
    public async Task Vote_OtherDbErrors_ArePropagated()
    {
        await using var db = CreateDb(new ThrowOnSaveInterceptor());
        await Assert.ThrowsAsync<DbUpdateException>(() => CreateService(db).VoteAsync(_diagnosticId, "user-1", true));
    }

    public void Dispose() => _connection.Dispose();

    private sealed class InsertCompetingVoteInterceptor : SaveChangesInterceptor
    {
        private readonly Func<AppDbContext> _factory;
        private readonly Guid _diagnosticId;
        private readonly string _userId;
        private readonly bool _isUseful;

        public bool Fired { get; private set; }

        public InsertCompetingVoteInterceptor(Func<AppDbContext> factory, Guid diagnosticId, string userId, bool isUseful)
        {
            _factory = factory;
            _diagnosticId = diagnosticId;
            _userId = userId;
            _isUseful = isUseful;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var addsVote = eventData.Context!.ChangeTracker.Entries<DiagnosticFeedback>().Any(e => e.State == EntityState.Added);
            if (!Fired && addsVote)
            {
                Fired = true;
                await using var other = _factory();
                other.DiagnosticFeedback.Add(new DiagnosticFeedback
                {
                    Id = Guid.NewGuid(),
                    DiagnosticId = _diagnosticId,
                    UserId = _userId,
                    IsUseful = _isUseful,
                    VotedAt = DateTime.UtcNow
                });
                await other.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
    }

    private sealed class ThrowOnSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => throw new DbUpdateException("disk I/O error");
    }
}
