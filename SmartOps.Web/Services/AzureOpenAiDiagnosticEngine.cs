using System;
using System.Data;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SmartOps.Core.Entities;
using SmartOps.Infrastructure.Data;

namespace SmartOps.Web.Services
{
    /// <summary>
    /// Real Adapter implementation of <see cref="ITransactionAnalyzer"/> that calls Azure OpenAI / OpenAI
    /// through Semantic Kernel, using the Provider/ApiKey/ModelId persisted from the /integrations page.
    /// Mirrors the same three-section Markdown contract produced by <see cref="DevFakeTransactionAnalyzer"/>
    /// so the UI does not need to change.
    /// </summary>
    public sealed class AzureOpenAiDiagnosticEngine : ITransactionAnalyzer
    {
        private const string ProviderKey = "Integrations.Provider";
        private const string ApiKeyKey = "Integrations.ApiKey";
        private const string ModelIdKey = "Integrations.ModelId";
        private const string EndpointKey = "Integrations.Endpoint";

        private readonly IChatCompletionServiceFactory _factory;
        private readonly AppDbContext? _db;

        public AzureOpenAiDiagnosticEngine(IChatCompletionServiceFactory factory, AppDbContext? db = null)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _db = db;
        }

        public async Task<string> AnalyzeAsync(Transaction tx)
        {
            if (tx == null) throw new ArgumentNullException(nameof(tx));

            var settings = ReadIntegrationSettings();

            if (string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                throw new AiEngineException(
                    "No hay una API Key configurada en /integrations para el proveedor de IA seleccionado.");
            }

            IChatCompletionService chatService;
            try
            {
                chatService = _factory.Create(settings.Provider, settings.ApiKey, settings.ModelId, settings.Endpoint);
            }
            catch (Exception ex)
            {
                throw new AiEngineException(
                    "No se pudo inicializar el conector de IA. Verificá el proveedor, el Model ID y (si aplica) el endpoint de Azure configurados en /integrations.",
                    ex);
            }

            var history = new ChatHistory();
            history.AddSystemMessage(
                "Eres un Agente de Soporte Técnico experto en FinTech y APIs de pagos (Stripe). " +
                "Respondé siempre en Markdown con exactamente estas tres secciones, en este orden: " +
                "'## 🔍 Razón del fallo', '## 🛠️ Acción recomendada para el operador', '## 📨 Mensaje sugerido para el cliente'.");
            history.AddUserMessage(BuildPrompt(tx));

            try
            {
                var results = await chatService.GetChatMessageContentsAsync(history);
                var content = results?.FirstOrDefault()?.Content;

                if (string.IsNullOrWhiteSpace(content))
                {
                    throw new AiEngineException("El proveedor de IA no devolvió contenido en la respuesta.");
                }

                return content;
            }
            catch (AiEngineException)
            {
                throw;
            }
            catch (HttpOperationException httpEx)
            {
                throw new AiEngineException(MapHttpErrorMessage(httpEx), httpEx);
            }
            catch (OperationCanceledException ex)
            {
                throw new AiEngineException(
                    "La solicitud al proveedor de IA superó el tiempo de espera (timeout). Intentá nuevamente en unos minutos.",
                    ex);
            }
            catch (Exception ex)
            {
                throw new AiEngineException(
                    $"Ocurrió un error inesperado al invocar al proveedor de IA: {ex.Message}",
                    ex);
            }
        }

        private static string MapHttpErrorMessage(HttpOperationException httpEx)
        {
            return httpEx.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    "La API Key configurada en /integrations no es válida o no tiene permisos para este modelo. Verificá las credenciales.",
                HttpStatusCode.TooManyRequests =>
                    "Se alcanzó el límite de solicitudes (rate limit) del proveedor de IA. Esperá unos minutos e intentá nuevamente.",
                HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout =>
                    "La solicitud al proveedor de IA superó el tiempo de espera (timeout). Intentá nuevamente en unos minutos.",
                _ => $"El proveedor de IA devolvió un error ({(httpEx.StatusCode.HasValue ? ((int)httpEx.StatusCode.Value).ToString() : httpEx.Message)}). Verificá la configuración en /integrations."
            };
        }

        private static string BuildPrompt(Transaction tx)
        {
            var sb = new StringBuilder();
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
            return sb.ToString();
        }

        private IntegrationSettings ReadIntegrationSettings()
        {
            var result = new IntegrationSettings("Local", string.Empty, "gpt-4o", null);

            if (_db == null)
            {
                return result;
            }

            try
            {
                var conn = _db.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open) conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Value FROM Settings WHERE Key = @k LIMIT 1";
                var param = cmd.CreateParameter();
                param.ParameterName = "@k";
                cmd.Parameters.Add(param);

                param.Value = ProviderKey;
                var provider = cmd.ExecuteScalar() as string ?? result.Provider;

                param.Value = ApiKeyKey;
                var apiKey = cmd.ExecuteScalar() as string ?? string.Empty;

                param.Value = ModelIdKey;
                var modelId = cmd.ExecuteScalar() as string ?? result.ModelId;

                param.Value = EndpointKey;
                var endpoint = cmd.ExecuteScalar() as string;

                return new IntegrationSettings(provider, apiKey, modelId, endpoint);
            }
            catch (Exception)
            {
                // Settings table may not exist yet (e.g. fresh DB); treat as unconfigured rather than crashing.
                return result;
            }
        }

        private sealed record IntegrationSettings(string Provider, string ApiKey, string ModelId, string? Endpoint);
    }
}
