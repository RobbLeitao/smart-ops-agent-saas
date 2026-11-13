using System;
using System.Net;
using Microsoft.SemanticKernel;

namespace SmartOps.Web.Services
{
    /// <summary>
    /// Single mapping from provider/SDK failures to operator-facing <see cref="AiEngineException"/> messages.
    /// Shared by the diagnostic engine (<see cref="AIOpsService"/>, shown in the Drawer) and
    /// «Probar conexión» (<see cref="AiConnectionTester"/>), so both say the same thing.
    /// Messages never contain the API key: when <c>apiKey</c> is given, any occurrence is masked.
    /// </summary>
    public static class AiEngineErrorMapper
    {
        public const string TimeoutMessage =
            "La solicitud al proveedor de IA superó el tiempo de espera (timeout). Intentá nuevamente en unos minutos.";

        public const string UnauthorizedMessage =
            "La API Key configurada en /integrations no es válida o no tiene permisos para este modelo. Verificá las credenciales.";

        public const string RateLimitMessage =
            "Se alcanzó el límite de solicitudes (rate limit) del proveedor de IA. Esperá unos minutos e intentá nuevamente.";

        public static string ModelNotFoundMessage(string? modelId)
            => string.IsNullOrWhiteSpace(modelId)
                ? "El modelo no existe o esta key no tiene acceso. Verificá el ID del modelo."
                : $"El modelo «{modelId}» no existe o esta key no tiene acceso. Verificá el ID del modelo.";

        public static AiEngineException Map(Exception ex, string? modelId = null, string? apiKey = null)
        {
            if (ex is AiEngineException aiEx)
            {
                return ContainsKey(aiEx.Message, apiKey) ? new AiEngineException(Mask(aiEx.Message, apiKey)) : aiEx;
            }

            // Inner exceptions may carry SDK text that echoes request data; when a key is known and leaks
            // anywhere in the chain, do not keep the original exception attached.
            var inner = ContainsKey(ex.ToString(), apiKey) ? null : ex;

            string message = ex switch
            {
                HttpOperationException httpEx => MapHttp(httpEx, modelId),
                OperationCanceledException => TimeoutMessage,
                _ => $"Ocurrió un error inesperado al invocar al proveedor de IA: {ex.Message}"
            };

            message = Mask(message, apiKey);
            return inner is null ? new AiEngineException(message) : new AiEngineException(message, inner);
        }

        private static string MapHttp(HttpOperationException httpEx, string? modelId)
        {
            return httpEx.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => UnauthorizedMessage,
                HttpStatusCode.TooManyRequests => RateLimitMessage,
                HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => TimeoutMessage,
                HttpStatusCode.NotFound when IsModelNotFound(httpEx) => ModelNotFoundMessage(modelId),
                _ => $"El proveedor de IA devolvió un error ({(httpEx.StatusCode.HasValue ? ((int)httpEx.StatusCode.Value).ToString() : httpEx.Message)}). Verificá la configuración en /integrations."
            };
        }

        private static bool IsModelNotFound(HttpOperationException httpEx)
            => (httpEx.ResponseContent?.Contains("model_not_found", StringComparison.OrdinalIgnoreCase) ?? false)
               || (httpEx.Message?.Contains("model_not_found", StringComparison.OrdinalIgnoreCase) ?? false)
               || (httpEx.InnerException?.Message?.Contains("model_not_found", StringComparison.OrdinalIgnoreCase) ?? false);

        private static bool ContainsKey(string? text, string? apiKey)
            => !string.IsNullOrEmpty(apiKey) && apiKey.Length >= 4 && text?.Contains(apiKey, StringComparison.Ordinal) == true;

        private static string Mask(string message, string? apiKey)
            => ContainsKey(message, apiKey) ? message.Replace(apiKey!, "••••", StringComparison.Ordinal) : message;
    }
}
