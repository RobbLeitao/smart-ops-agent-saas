using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartOps.Core.Entities;
using SmartOps.Infrastructure.Data;

namespace SmartOps.Web.Services;

/// <summary>
/// Voto «¿Te sirvió este diagnóstico?» por usuario, compartido por el Historial y el Drawer de Detalle.
/// Un voto por (DiagnosticId, UserId), garantizado por el índice único de <c>DiagnosticFeedback</c>.
/// </summary>
public sealed class DiagnosticFeedbackService
{
    private readonly AppDbContext _db;
    private readonly ILogger<DiagnosticFeedbackService> _logger;
    private readonly DbContextGate? _gate;

    public DiagnosticFeedbackService(AppDbContext db, ILogger<DiagnosticFeedbackService> logger, DbContextGate? gate = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gate = gate;
    }

    /// <summary>Voto actual del usuario: true (Sí), false (No) o null si todavía no votó.</summary>
    public Task<bool?> GetUserVoteAsync(Guid diagnosticId, string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return Task.FromResult<bool?>(null);

        return RunAsync(async () =>
        {
            var vote = await _db.DiagnosticFeedback.AsNoTracking()
                .Where(x => x.DiagnosticId == diagnosticId && x.UserId == userId)
                .Select(x => (bool?)x.IsUseful)
                .FirstOrDefaultAsync();
            return vote;
        });
    }

    /// <summary>
    /// Crea o cambia el voto del usuario. Si otro request del mismo usuario insertó el voto entre la lectura
    /// y el guardado (violación del índice único DiagnosticId+UserId), reintenta como UPDATE sobre la fila
    /// ganadora. Cualquier otro error se loguea y se propaga.
    /// </summary>
    public Task<DiagnosticFeedback> VoteAsync(Guid diagnosticId, string userId, bool isUseful)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("Se necesita un usuario para votar.", nameof(userId));

        return RunAsync(async () =>
        {
            var feedback = await _db.DiagnosticFeedback
                .FirstOrDefaultAsync(x => x.DiagnosticId == diagnosticId && x.UserId == userId);

            if (feedback == null)
            {
                feedback = new DiagnosticFeedback
                {
                    Id = Guid.NewGuid(),
                    DiagnosticId = diagnosticId,
                    UserId = userId,
                    IsUseful = isUseful,
                    VotedAt = DateTime.UtcNow
                };
                _db.DiagnosticFeedback.Add(feedback);

                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    _logger.LogInformation(ex, "Concurrent vote for diagnostic {DiagnosticId} by {UserId}; retrying as UPDATE.", diagnosticId, userId);

                    _db.Entry(feedback).State = EntityState.Detached;
                    feedback = await _db.DiagnosticFeedback
                        .FirstAsync(x => x.DiagnosticId == diagnosticId && x.UserId == userId);
                    ApplyVote(feedback, isUseful);
                    await _db.SaveChangesAsync();
                }
            }
            else
            {
                ApplyVote(feedback, isUseful);
                await _db.SaveChangesAsync();
            }

            // Campo legado: se sigue escribiendo como hasta ahora (tiene su propia tarea de limpieza).
            var diagnostic = await _db.Diagnostics.FirstOrDefaultAsync(d => d.Id == diagnosticId);
            if (diagnostic != null)
            {
                diagnostic.WasHelpful = isUseful;
                await _db.SaveChangesAsync();
            }

            return feedback;
        });
    }

    private static void ApplyVote(DiagnosticFeedback feedback, bool isUseful)
    {
        feedback.IsUseful = isUseful;
        feedback.VotedAt = DateTime.UtcNow;
    }

    internal static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (Exception? e = ex.InnerException; e != null; e = e.InnerException)
        {
            // SQLite: SQLITE_CONSTRAINT (19) / SQLITE_CONSTRAINT_UNIQUE (2067).
            if (e is SqliteException sqlite && sqlite.SqliteErrorCode == 19
                && (sqlite.SqliteExtendedErrorCode == 2067 || sqlite.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (e.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return _gate is null ? await action() : await _gate.RunAsync(action);
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            _logger.LogError(ex, "Diagnostic feedback operation failed.");
            throw;
        }
    }
}
