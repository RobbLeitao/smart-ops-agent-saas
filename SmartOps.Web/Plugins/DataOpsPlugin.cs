using System.ComponentModel;
using System.Text;
using Microsoft.SemanticKernel.SkillDefinition;
using SmartOps.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace SmartOps.Web.Plugins;

public sealed class DataOpsPlugin
{
    private readonly AppDbContext _db;

    public DataOpsPlugin(AppDbContext db)
    {
        _db = db ?? throw new System.ArgumentNullException(nameof(db));
    }

    [KernelFunction]
    [Description("Obtiene transacciones fallidas de Stripe y devuelve una lista formateada con Id, Amount, ErrorMessage y OccurredAt para que el agente las lea.")]
    public async Task<string> GetFailedTransactionsAsync()
    {
        var failed = await _db.Transactions
            .Where(t => t.Status == "Failed" && t.Provider == "Stripe")
            .OrderByDescending(t => t.OccurredAt)
            .ToListAsync();

        if (failed.Count == 0)
        {
            return "No failed Stripe transactions found.";
        }

        var sb = new StringBuilder();
        foreach (var t in failed)
        {
            sb.AppendLine($"Id: {t.Id}");
            sb.AppendLine($"Amount: {t.Amount} {t.Currency}");
            sb.AppendLine($"Error: {t.ErrorMessage ?? "(no message)"}");
            sb.AppendLine($"OccurredAt: {t.OccurredAt?.ToString("o") ?? "(unknown)"}");
            sb.AppendLine("---");
        }

        return sb.ToString();
    }
}
