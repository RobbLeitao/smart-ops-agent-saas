using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace SmartOps.Web.Services;

public sealed class DiagnosticOrchestratorService
{
    private readonly IAIOpsService _aiOps;
    private readonly SmartOps.Infrastructure.Data.AppDbContext? _db;

    public DiagnosticOrchestratorService(IAIOpsService aiOps, SmartOps.Infrastructure.Data.AppDbContext? db = null)
    {
        _aiOps = aiOps ?? throw new ArgumentNullException(nameof(aiOps));
        _db = db;
    }

    public async Task<string> RunDiagnosticAsync(Guid transactionId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SYSTEM: Eres un Agente de Soporte Técnico experto en FinTech y APIs de Stripe.");
        sb.AppendLine("Usa el plugin de datos para obtener los detalles de la transacción fallida usando el transactionId proporcionado.");
        sb.AppendLine("Analiza el código de error devuelto por Stripe y genera una respuesta estructurada en Markdown con las secciones solicitadas.");
        sb.AppendLine();
        sb.AppendLine($"TransactionId: {transactionId}");

        var prompt = sb.ToString();
        try
        {
            var response = await _aiOps.ExecutePromptAsync(prompt);

            if (!string.IsNullOrWhiteSpace(response) && _db != null)
            {
                var txs = await _db.Transactions.ToListAsync();
                var tx = txs.FirstOrDefault(t => CreateDeterministicGuidForInt(t.Id) == transactionId);
                if (tx != null)
                {
                    await PersistDiagnosticAsync(tx, response);
                }
            }

            return response;
        }
        catch (Exception)
        {
            return "## 🔍 Resumen del Error\nNo se pudo ejecutar el proveedor de IA. Esto es una respuesta simulada para pruebas de UI.\n\n## 🛠️ Acciones Recomendadas para el Operador\n- Verificar configuración de AI (appsettings o variables de entorno).\n\n## 📨 Mensaje sugerido para el cliente\nEstimado cliente, estamos investigando su pago y le informaremos pronto.";
        }
    }

    public async Task<string> RunDiagnosticAsync(SmartOps.Core.Entities.Transaction tx)
    {
        if (tx == null) throw new ArgumentNullException(nameof(tx));

        var sb = new StringBuilder();
        sb.AppendLine("SYSTEM: Eres un Agente de Soporte Técnico experto en FinTech y APIs de Stripe.");
        sb.AppendLine("Analiza la transacción fallida proporcionada y devuelve un diagnóstico estructurado en Markdown con: ");
        sb.AppendLine("- Razón del fallo");
        sb.AppendLine("- Acción recomendada para el operador");
        sb.AppendLine("- Mensaje sugerido para enviar al cliente");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine($"Id: {tx.Id}");
        sb.AppendLine($"CustomerId: {tx.CustomerId}");
        sb.AppendLine($"Amount: {tx.Amount} {tx.Currency}");
        sb.AppendLine($"Provider: {tx.Provider}");
        sb.AppendLine($"GatewayReference: {tx.GatewayReference}");
        sb.AppendLine($"CardLast4: {tx.CardLast4}");
        sb.AppendLine($"Status: {tx.Status}");
        sb.AppendLine($"ErrorMessage: {tx.ErrorMessage}");
        sb.AppendLine($"OccurredAt: {tx.OccurredAt:o}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("Por favor responde únicamente con Markdown estructurado que contenga las tres secciones solicitadas.");

        var prompt = sb.ToString();
        try
        {
            var response = await _aiOps.ExecutePromptAsync(prompt);
            if (!string.IsNullOrWhiteSpace(response) && _db != null)
            {
                await PersistDiagnosticAsync(tx, response);
            }

            return response;
        }
        catch (Exception)
        {
            return "## 🔍 Resumen del Error\nNo se pudo ejecutar el proveedor de IA. Esto es una respuesta simulada para pruebas de UI.\n\n## 🛠️ Acciones Recomendadas para el Operador\n- Verificar configuración de AI (appsettings o variables de entorno).\n\n## 📨 Mensaje sugerido para el cliente\nEstimado cliente, estamos investigando su pago y le informaremos pronto.";
        }
    }

    private async Task PersistDiagnosticAsync(SmartOps.Core.Entities.Transaction tx, string response)
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
            Markdown = response,
            CardLast4 = tx.CardLast4
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
