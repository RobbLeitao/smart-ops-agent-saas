using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace SmartOps.Web.Services
{
    public sealed record ConnectionTestResult(bool Ok, string Message);

    public interface IAiConnectionTester
    {
        /// <summary>
        /// Minimal call to the model with the given (unsaved) form values. Never persists anything and never
        /// logs or returns the API key.
        /// </summary>
        Task<ConnectionTestResult> TestAsync(string provider, string apiKey, string modelId, string? endpoint, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// «Probar conexión» in /integrations. Builds the connector through <see cref="IChatCompletionServiceFactory"/>
    /// (same path as the diagnostic engine) and sends one short message with a small token budget. Any successful
    /// (HTTP 200) answer counts as a working connection, even with empty content: reasoning models such as gpt-oss can
    /// spend the whole budget thinking. Failures use <see cref="AiEngineErrorMapper"/>, like the Drawer.
    /// </summary>
    public sealed class AiConnectionTester : IAiConnectionTester
    {
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
        public const int MaxTokens = 16;

        private readonly IChatCompletionServiceFactory _factory;
        private readonly ILogger<AiConnectionTester>? _logger;
        private readonly TimeSpan _timeout;

        public AiConnectionTester(IChatCompletionServiceFactory factory, ILogger<AiConnectionTester>? logger = null)
            : this(factory, logger, DefaultTimeout)
        {
        }

        public AiConnectionTester(IChatCompletionServiceFactory factory, ILogger<AiConnectionTester>? logger, TimeSpan timeout)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _logger = logger;
            _timeout = timeout;
        }

        public async Task<ConnectionTestResult> TestAsync(string provider, string apiKey, string modelId, string? endpoint, CancellationToken cancellationToken = default)
        {
            var model = modelId?.Trim() ?? string.Empty;
            apiKey = apiKey?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new(false, "Ingresá la API key para probar la conexión.");
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                return new(false, "Ingresá el modelo para probar la conexión.");
            }

            IChatCompletionService chat;
            try
            {
                chat = _factory.Create(provider, apiKey, model, string.IsNullOrWhiteSpace(endpoint) ? null : endpoint.Trim());
            }
            catch (ArgumentException ex)
            {
                // Our own validation messages (endpoint), which never include the key.
                Log(provider, model, "invalid-config");
                var text = ex.ParamName is null ? ex.Message : ex.Message.Replace($" (Parameter '{ex.ParamName}')", string.Empty);
                return new(false, AiEngineErrorMapper.Map(new AiEngineException(text), model, apiKey).Message);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_timeout);

            try
            {
                var history = new ChatHistory();
                history.AddUserMessage("ping");
                var settings = new OpenAIPromptExecutionSettings { MaxTokens = MaxTokens };

                await chat.GetChatMessageContentsAsync(history, settings, kernel: null, timeoutCts.Token);

                Log(provider, model, "ok");
                return new(true, $"Conexión correcta con {model}");
            }
            catch (Exception ex)
            {
                // A cancellation from our own timer is a timeout, whatever the SDK wrapped it in.
                var mapped = timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested
                    ? new AiEngineException(AiEngineErrorMapper.TimeoutMessage)
                    : AiEngineErrorMapper.Map(ex, model, apiKey);

                Log(provider, model, ex.GetType().Name);
                return new(false, mapped.Message);
            }
        }

        private void Log(string provider, string model, string result)
        {
            try
            {
                _logger?.LogInformation("AI connection test provider={Provider} model={Model} result={Result}", provider, model, result);
            }
            catch
            {
                // never fail the test because of logging
            }
        }
    }
}
