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
        // Build system prompt according to rules
        var sb = new StringBuilder();
        sb.AppendLine("SYSTEM: Eres un Agente de Soporte Técnico experto en FinTech y APIs de Stripe.");
        sb.AppendLine("Usa el plugin de datos para obtener los detalles de la transacción fallida usando el transactionId proporcionado.");
        sb.AppendLine("Analiza el código de error devuelto por Stripe y genera una respuesta estructurada en Markdown con las secciones solicitadas.");
        sb.AppendLine();
        sb.AppendLine($"TransactionId: {transactionId}");

        var prompt = sb.ToString();

        var response = await _aiOps.ExecutePromptAsync(prompt);

        return response;
    }
}
