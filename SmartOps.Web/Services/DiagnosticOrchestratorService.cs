using System.Text;
using SmartOps.Web.Services;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace SmartOps.Web.Services;

public sealed class DiagnosticOrchestratorService
{
    private readonly IAIOpsService _aiOps;
    private readonly SmartOps.Infrastructure.Data.AppDbContext? _db;

    public DiagnosticOrchestratorService(IAIOpsService aiOps, SmartOps.Infrastructure.Data.AppDbContext? db = null)
    {
        _aiOps = aiOps ?? throw new ArgumentNullException(nameof(aiOps));
        // DbContext is optional for test doubles; when present, persistence will occur.
        _db = db;
    }

    public async Task<string> RunDiagnosticAsync(Guid transactionId)
    {
        // Backwards-compatibility: if callers only provide an ID, keep existing behavior that asks the AI to use the data plugin.
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

            // Best-effort: if we can map this Guid to a Transaction, persist the diagnostic
            try
            {
                if (!string.IsNullOrWhiteSpace(response) && _db != null)
                {
                    // Attempt to find a transaction whose deterministic guid matches this guid
                    var txs = await _db.Transactions.ToListAsync();
                    int mappedId = 0;
                    foreach (var t in txs)
                    {
                        var guidFor = CreateDeterministicGuidForInt(t.Id);
                        if (guidFor == transactionId)
                        {
                            mappedId = t.Id;
                            break;
                        }
                    }

                    // Determine card last4: try to read from transaction if available, otherwise generate a random 4-digit string for testing.
                    string last4 = "";
                    var mappedTx = txs.FirstOrDefault(t => t.Id == mappedId);
                    if (mappedTx != null)
                    {
                        // If Transaction has a CardLast4 property in future, prefer it; otherwise generate.
                        var prop = mappedTx.GetType().GetProperty("CardLast4");
                        if (prop != null)
                        {
                            last4 = prop.GetValue(mappedTx)?.ToString() ?? string.Empty;
                        }
                    }
                    if (string.IsNullOrWhiteSpace(last4))
                    {
                        var rnd = new Random();
                        last4 = rnd.Next(0, 10000).ToString("D4");
                    }

                    var diag = new SmartOps.Core.Entities.Diagnostic
                    {
                        Id = Guid.NewGuid(),
                        TransactionId = mappedId,
                        CreatedAt = DateTime.UtcNow,
                        Markdown = response,
                        CardLast4 = last4
                    };
                    _db.Diagnostics.Add(diag);
                    await _db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to save diagnostic: {ex}");
            }

            return response;
        }
        catch (Exception)
        {
            return "## 🔍 Resumen del Error\nNo se pudo ejecutar el proveedor de IA. Esto es una respuesta simulada para pruebas de UI.\n\n## 🛠️ Acciones Recomendadas para el Operador\n- Verificar configuración de AI (appsettings o variables de entorno).\n\n## 📨 Mensaje sugerido para el cliente\nEstimado cliente, estamos investigando su pago y le informaremos pronto.";
        }
    }

    // New overload: accept a Transaction entity and build an explicit prompt including full transaction fields.
    public async Task<string> RunDiagnosticAsync(SmartOps.Core.Entities.Transaction tx)
    {
        if (tx == null) throw new ArgumentNullException(nameof(tx));

        var sb = new StringBuilder();
        sb.AppendLine("SYSTEM: Eres un Agente de Soporte Técnico experto en FinTech y APIs de Stripe.");
        sb.AppendLine("Analiza la transacción fallida proporcionada y devuelve un diagnóstico estructurado en Markdown con: \n- Razón del fallo\n- Acción recomendada para el operador\n- Mensaje sugerido para enviar al cliente");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine($"Id: {tx.Id}");
        sb.AppendLine($"CustomerId: {tx.CustomerId}");
        sb.AppendLine($"Amount: {tx.Amount} {tx.Currency}");
        sb.AppendLine($"Provider: {tx.Provider}");
        sb.AppendLine($"GatewayReference: {tx.GatewayReference}");
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

            // Persist diagnostic to DB if available
            try
            {
                if (!string.IsNullOrWhiteSpace(response) && _db != null)
                {
                    // Prefer a last4 stored on the transaction if present; otherwise generate a random one for testing.
                    string last4 = string.Empty;
                    var prop = tx.GetType().GetProperty("CardLast4");
                    if (prop != null)
                    {
                        last4 = prop.GetValue(tx)?.ToString() ?? string.Empty;
                    }
                    if (string.IsNullOrWhiteSpace(last4))
                    {
                        var rnd = new Random();
                        last4 = rnd.Next(0, 10000).ToString("D4");
                    }

                    var diag = new SmartOps.Core.Entities.Diagnostic
                    {
                        Id = Guid.NewGuid(),
                        TransactionId = tx.Id,
                        CreatedAt = DateTime.UtcNow,
                        Markdown = response,
                        CardLast4 = last4
                    };
                    _db.Diagnostics.Add(diag);
                    await _db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                // Swallow persistence errors but log to console for dev visibility
                Console.Error.WriteLine($"Failed to save diagnostic: {ex}");
            }

            return response;
        }
        catch (Exception)
        {
            return "## 🔍 Resumen del Error\nNo se pudo ejecutar el proveedor de IA. Esto es una respuesta simulada para pruebas de UI.\n\n## 🛠️ Acciones Recomendadas para el Operador\n- Verificar configuración de AI (appsettings o variables de entorno).\n\n## 📨 Mensaje sugerido para el cliente\nEstimado cliente, estamos investigando su pago y le informaremos pronto.";
        }
    }

    private static Guid CreateDeterministicGuidForInt(int id)
    {
        var ns = System.Text.Encoding.UTF8.GetBytes("SmartOps.Transaction.IntMapping");
        var idBytes = System.Text.Encoding.UTF8.GetBytes(id.ToString());
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(ns.Concat(idBytes).ToArray());
        return new Guid(hash.Take(16).ToArray());
    }
}
