using System.Text;
using SmartOps.Web.Services;

namespace SmartOps.Web.Services;

public sealed class DiagnosticOrchestratorService
{
    private readonly IAIOpsService _aiOps;

    public DiagnosticOrchestratorService(IAIOpsService aiOps)
    {
        _aiOps = aiOps ?? throw new ArgumentNullException(nameof(aiOps));
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
            return response;
        }
        catch (Exception)
        {
            return "## 🔍 Resumen del Error\nNo se pudo ejecutar el proveedor de IA. Esto es una respuesta simulada para pruebas de UI.\n\n## 🛠️ Acciones Recomendadas para el Operador\n- Verificar configuración de AI (appsettings o variables de entorno).\n\n## 📨 Mensaje sugerido para el cliente\nEstimado cliente, estamos investigando su pago y le informaremos pronto.";
        }
    }
}
