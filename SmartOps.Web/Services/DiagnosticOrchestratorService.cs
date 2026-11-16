using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SmartOps.Web.Services;

public sealed class DiagnosticOrchestratorService
{
    /// <summary>
    /// Shown by the Drawer / webhook when something other than the AI provider fails. Deliberately not a diagnostic:
    /// a fallback in the v2 format would look like a real answer.
    /// </summary>
    public const string UnexpectedErrorMessage = "Ocurrió un error inesperado al generar el diagnóstico. Probá de nuevo.";

    private readonly IAIOpsService _aiOps;
    private readonly SmartOps.Infrastructure.Data.AppDbContext? _db;
    private readonly ILogger<DiagnosticOrchestratorService> _logger;

    public DiagnosticOrchestratorService(
        IAIOpsService aiOps,
        SmartOps.Infrastructure.Data.AppDbContext? db = null,
        ILogger<DiagnosticOrchestratorService>? logger = null)
    {
        _aiOps = aiOps ?? throw new ArgumentNullException(nameof(aiOps));
        _db = db;
        _logger = logger ?? NullLogger<DiagnosticOrchestratorService>.Instance;
    }

    /// <summary>
    /// Webhook entry point. The transaction is resolved in the database and analyzed with the v2 prompt, like the
    /// Drawer. Only when it is not found (or there is no database) the legacy prompt is used, which asks the model
    /// to look it up through DataOpsPlugin.
    /// </summary>
    public async Task<string> RunDiagnosticAsync(Guid transactionId)
    {
        if (_db != null)
        {
            var txs = await _db.Transactions.ToListAsync();
            var tx = txs.FirstOrDefault(t => CreateDeterministicGuidForInt(t.Id) == transactionId);
            if (tx != null)
            {
                return await RunCoreAsync(tx, createdByUserId: null);
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("SYSTEM: Eres un Agente de Soporte Técnico experto en FinTech y APIs de Stripe.");
        sb.AppendLine("Usa el plugin de datos para obtener los detalles de la transacción fallida usando el transactionId proporcionado.");
        sb.AppendLine("Analiza el código de error devuelto por Stripe y genera una respuesta estructurada en Markdown con las secciones solicitadas.");
        sb.AppendLine();
        sb.AppendLine($"TransactionId: {transactionId}");

        try
        {
            return await _aiOps.ExecutePromptAsync(sb.ToString());
        }
        catch (AiEngineException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Unexpected(ex, null);
        }
    }

    public Task<string> RunDiagnosticAsync(SmartOps.Core.Entities.Transaction tx)
    {
        if (tx == null) throw new ArgumentNullException(nameof(tx));
        return RunCoreAsync(tx, createdByUserId: null);
    }

    public Task<string> RunDiagnosticAsync(SmartOps.Core.Entities.Transaction tx, string? createdByUserId)
    {
        if (tx == null) throw new ArgumentNullException(nameof(tx));
        return RunCoreAsync(tx, createdByUserId);
    }

    /// <summary>
    /// Prompt v2 → engine → Diagnostic row (with PromptVersion). Provider errors propagate as they are; anything else
    /// is logged and becomes <see cref="UnexpectedErrorMessage"/>. A Diagnostic is persisted only on success.
    /// </summary>
    private async Task<string> RunCoreAsync(SmartOps.Core.Entities.Transaction tx, string? createdByUserId)
    {
        try
        {
            var prompt = DiagnosticPromptBuilder.Build(tx);
            var result = await _aiOps.ExecuteDiagnosticAsync(prompt);

            if (!string.IsNullOrWhiteSpace(result.Markdown) && _db != null)
            {
                await PersistDiagnosticAsync(tx, result, createdByUserId);
            }

            return result.Markdown;
        }
        catch (AiEngineException)
        {
            // Operator-facing provider error: propagate to the caller (UI / webhook) without persisting a Diagnostic.
            throw;
        }
        catch (Exception ex)
        {
            throw Unexpected(ex, tx.Id);
        }
    }

    // Only the exception type and stack trace are logged: messages can echo prompt data or provider responses.
    private AiEngineException Unexpected(Exception ex, int? transactionId)
    {
        _logger.LogError(
            "Unexpected error generating diagnostic for transaction {TransactionId}: {ExceptionType}\n{StackTrace}",
            transactionId?.ToString() ?? "(unknown)",
            ex.GetType().FullName,
            ex.StackTrace);
        return new AiEngineException(UnexpectedErrorMessage);
    }

    private async Task PersistDiagnosticAsync(SmartOps.Core.Entities.Transaction tx, AiDiagnosticResult result, string? createdByUserId)
    {
        if (string.IsNullOrWhiteSpace(tx.CardLast4))
        {
            throw new InvalidOperationException($"Transaction {tx.Id} is missing CardLast4.");
        }

        var diag = new SmartOps.Core.Entities.Diagnostic
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            CreatedAt = DateTime.UtcNow,
            Markdown = result.Markdown,
            CardLast4 = tx.CardLast4,
            CreatedByUserId = createdByUserId,
            PromptVersion = result.PromptVersion
        };

        _db!.Diagnostics.Add(diag);
        await _db.SaveChangesAsync();
    }

    private static Guid CreateDeterministicGuidForInt(int id)
    {
        var ns = Encoding.UTF8.GetBytes("SmartOps.Transaction.IntMapping");
        var idBytes = Encoding.UTF8.GetBytes(id.ToString());
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(ns.Concat(idBytes).ToArray());
        return new Guid(hash.Take(16).ToArray());
    }
}
