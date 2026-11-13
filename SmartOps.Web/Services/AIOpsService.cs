using System;
using System.Data;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace SmartOps.Web.Services
{
    public interface IAIOpsService
    {
        /// <summary>
        /// Executes the prompt against the AI provider configured in /integrations.
        /// </summary>
        /// <exception cref="AiEngineException">
        /// A real provider (OpenAI / Azure OpenAI) is configured and the call could not be completed
        /// (missing API key, invalid credentials, rate limit, timeout, empty response, ...). The message
        /// is operator-facing and safe to show in the UI.
        /// </exception>
        Task<string> ExecutePromptAsync(string userPrompt);
    }

    /// <summary>
    /// Single AI diagnostic engine. Provider selection, per call, from the settings persisted in /integrations:
    /// <list type="bullet">
    /// <item><description>"OpenAI" or any provider containing "azure": builds a connector through
    /// <see cref="IChatCompletionServiceFactory"/> with the persisted ApiKey / ModelId / Endpoint. Failures are
    /// surfaced as <see cref="AiEngineException"/>; there is no silent fallback to the simulator.</description></item>
    /// <item><description>"Local" or nothing configured: uses the injected application <see cref="Kernel"/>
    /// (environment-configured provider, if any) and, when it has no AI service, the Local Simulator.</description></item>
    /// </list>
    /// </summary>
    public sealed class AIOpsService : IAIOpsService
    {
        private const string DefaultModelId = "gpt-4o";

        // Artificial "thinking" delay applied only to the Local Simulator path below, so the AI diagnostic
        // feels like real background work and callers' loading UI (e.g. TransactionDetailDrawer's loader)
        // has time to be perceived. Real provider calls are never delayed here.
        private static readonly TimeSpan SimulatedThinkingDelay = TimeSpan.FromMilliseconds(1800);

        private readonly Kernel _kernel;
        private readonly SmartOps.Infrastructure.Data.AppDbContext? _db;
        private readonly IChatCompletionServiceFactory _chatFactory;

        public AIOpsService(
            Kernel kernel,
            SmartOps.Infrastructure.Data.AppDbContext? db = null,
            IChatCompletionServiceFactory? chatFactory = null)
        {
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _db = db;
            _chatFactory = chatFactory ?? new SemanticKernelChatCompletionServiceFactory();
        }

        public async Task<string> ExecutePromptAsync(string userPrompt)
        {
            if (userPrompt is null) throw new ArgumentNullException(nameof(userPrompt));

            var settings = new PromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };

            var integration = ReadIntegrationSettings();
            if (IsRealProvider(integration.Provider))
            {
                return await ExecuteWithConfiguredProviderAsync(integration, userPrompt, settings);
            }

            return await ExecuteWithDefaultKernelAsync(userPrompt, settings);
        }

        private async Task<string> ExecuteWithConfiguredProviderAsync(
            IntegrationSettings integration,
            string userPrompt,
            PromptExecutionSettings settings)
        {
            if (string.IsNullOrWhiteSpace(integration.ApiKey))
            {
                throw new AiEngineException(
                    "No hay una API Key configurada en /integrations para el proveedor de IA seleccionado.");
            }

            Kernel providerKernel;
            try
            {
                var chatService = _chatFactory.Create(
                    integration.Provider!,
                    integration.ApiKey,
                    integration.ModelId,
                    integration.Endpoint);

                var builder = Kernel.CreateBuilder();
                builder.Services.AddSingleton<IChatCompletionService>(chatService);
                providerKernel = builder.Build();
            }
            catch (Exception ex)
            {
                throw new AiEngineException(
                    "No se pudo inicializar el conector de IA. Verificá el proveedor, el Model ID y (si aplica) el endpoint configurados en /integrations.",
                    ex);
            }

            var content = await InvokeAndMapErrorsAsync(providerKernel, userPrompt, settings);
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new AiEngineException("El proveedor de IA no devolvió contenido en la respuesta.");
            }

            return content;
        }

        private async Task<string> ExecuteWithDefaultKernelAsync(string userPrompt, PromptExecutionSettings settings)
        {
            try
            {
                var result = await _kernel.InvokePromptAsync(userPrompt, new(settings));
                return result.ToString();
            }
            catch (Exception ex) when (IsNoAiServiceConfigured(ex))
            {
                // No AI provider configured at all (no persisted real provider and no chat completion service on
                // the application Kernel): Local Simulator.
                await Task.Delay(SimulatedThinkingDelay);
                return BuildSimulatedDiagnostic(userPrompt);
            }
            catch (Exception ex)
            {
                // The application Kernel has a real (environment-configured) provider and the call failed.
                throw MapToAiEngineException(ex);
            }
        }

        private static async Task<string> InvokeAndMapErrorsAsync(Kernel kernel, string userPrompt, PromptExecutionSettings settings)
        {
            try
            {
                var result = await kernel.InvokePromptAsync(userPrompt, new(settings));
                return result.ToString();
            }
            catch (Exception ex)
            {
                throw MapToAiEngineException(ex);
            }
        }

        private static bool IsNoAiServiceConfigured(Exception ex)
            => ex is KernelException
               && ex.Message != null
               && ex.Message.Contains("No service was found", StringComparison.OrdinalIgnoreCase);

        private static AiEngineException MapToAiEngineException(Exception ex)
        {
            return ex switch
            {
                AiEngineException aiEx => aiEx,
                HttpOperationException httpEx => new AiEngineException(MapHttpErrorMessage(httpEx), httpEx),
                OperationCanceledException => new AiEngineException(
                    "La solicitud al proveedor de IA superó el tiempo de espera (timeout). Intentá nuevamente en unos minutos.",
                    ex),
                _ => new AiEngineException(
                    $"Ocurrió un error inesperado al invocar al proveedor de IA: {ex.Message}",
                    ex)
            };
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

        private static bool IsRealProvider(string? provider)
            => !string.IsNullOrWhiteSpace(provider)
               && (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
                   || provider.Contains("azure", StringComparison.OrdinalIgnoreCase));

        private IntegrationSettings ReadIntegrationSettings()
        {
            var empty = new IntegrationSettings(null, string.Empty, DefaultModelId, null);
            if (_db == null) return empty;

            try
            {
                // Per-provider credentials (IntegrationSettingsStore): the active provider's own values only.
                var active = new IntegrationSettingsStore(_db).ReadActive();
                var c = active.Credentials;

                return new IntegrationSettings(
                    active.Provider,
                    c.ApiKey,
                    string.IsNullOrWhiteSpace(c.ModelId) ? DefaultModelId : c.ModelId,
                    string.IsNullOrWhiteSpace(c.Endpoint) ? null : c.Endpoint);
            }
            catch (Exception ex)
            {
                // Settings table may not exist yet (fresh DB / test providers): treat as unconfigured.
                Console.Error.WriteLine($"Failed reading integrations settings for AI: {ex.GetType().Name}");
                return empty;
            }
        }

        // Local Simulator: heuristic diagnosis based on the "ErrorMessage:" line of the prompt.
        private static string BuildSimulatedDiagnostic(string userPrompt)
        {
            string? errorLine = null;
            using (var sr = new StringReader(userPrompt))
            {
                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.TrimStart().StartsWith("ErrorMessage:", StringComparison.OrdinalIgnoreCase))
                    {
                        errorLine = line.Substring(line.IndexOf(':') + 1).Trim();
                        break;
                    }
                }
            }

            const string notConfigured =
                "## 🔍 Resumen del Error\nNo hay proveedor de IA configurado. Resultado simulado.\n\n## 🛠️ Acciones Recomendadas para el Operador\n- Revisar la configuración de AI en appsettings.json o las variables de entorno.\n\n## 📨 Mensaje sugerido para el cliente\nEstimado cliente, estamos investigando su pago.";

            if (string.IsNullOrWhiteSpace(errorLine))
            {
                return notConfigured;
            }

            var lower = errorLine.ToLowerInvariant();
            string reason, actions, customer;

            if (lower.Contains("expired") || lower.Contains("venc"))
            {
                reason = "Tarjeta vencida";
                actions = "- Solicitar al cliente que actualice la fecha de vencimiento.\n- Reintentar el cobro tras actualizar los datos.";
                customer = "Estimado cliente, su tarjeta ha expirado. Por favor actualice la fecha de vencimiento y reintente el pago.";
            }
            else if (lower.Contains("insufficient") || lower.Contains("fondos"))
            {
                reason = "Fondos insuficientes";
                actions = "- Sugerir reintentar en 24 horas.\n- Ofrecer cambiar de método de pago.";
                customer = "Estimado cliente, su tarjeta no tiene fondos suficientes en este momento. Puede intentar nuevamente más tarde o utilizar otro método de pago.";
            }
            else if (lower.Contains("suspected") || lower.Contains("fraud") || lower.Contains("sospecha"))
            {
                reason = "Posible fraude detectado";
                actions = "- Bloquear la transacción provisionalmente.\n- Revisar IP, país y patrones de comportamiento.\n- Contactar al cliente por verificación.";
                customer = "Hemos detectado actividad sospechosa en su pago. Nos comunicaremos para verificar la transacción.";
            }
            else if (lower.Contains("declined"))
            {
                reason = "Emisor rechazó la tarjeta";
                actions = "- Revisar con el emisor por código de rechazo.\n- Sugerir al cliente intentar con otra tarjeta.";
                customer = "Su emisor ha rechazado la transacción. Por favor intente con otra tarjeta o contacte a su banco.";
            }
            else if (lower.Contains("cvc"))
            {
                reason = "Código CVC inválido";
                actions = "- Verificar que el cliente ingresó correctamente el CVC.\n- Permitir reintento inmediato.";
                customer = "Parece que el código de seguridad (CVC) es incorrecto. Por favor verifique sus datos e intente nuevamente.";
            }
            else
            {
                return notConfigured;
            }

            return $"## 🔍 Razón del fallo\n{reason}\n\n## 🛠️ Acción recomendada para el operador\n{actions}\n\n## 📨 Mensaje sugerido para el cliente\n{customer}";
        }

        private sealed record IntegrationSettings(string? Provider, string ApiKey, string ModelId, string? Endpoint);
    }
}
