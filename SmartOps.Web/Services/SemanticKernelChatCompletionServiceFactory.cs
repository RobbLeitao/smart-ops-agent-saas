using System;
using System.Net.Http;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace SmartOps.Web.Services
{
    /// <summary>
    /// Production implementation of <see cref="IChatCompletionServiceFactory"/>. Builds a real
    /// Semantic Kernel connector (OpenAI or Azure OpenAI) from the credentials persisted in /integrations.
    /// For "OpenAI", an optional custom endpoint targets any OpenAI-compatible API (e.g. Groq).
    /// </summary>
    public sealed class SemanticKernelChatCompletionServiceFactory : IChatCompletionServiceFactory
    {
        private readonly HttpClient? _httpClient;

        /// <param name="httpClient">
        /// Optional HttpClient passed to the connector. Null in production (the connector creates its own); tests
        /// inject one with a fake handler to assert the target URL without real network calls.
        /// </param>
        public SemanticKernelChatCompletionServiceFactory(HttpClient? httpClient = null)
        {
            _httpClient = httpClient;
        }

        public IChatCompletionService Create(string provider, string apiKey, string modelId, string? endpoint)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("API key is required.", nameof(apiKey));
            }

            var effectiveModelId = string.IsNullOrWhiteSpace(modelId) ? "gpt-4o" : modelId;
            var builder = Kernel.CreateBuilder();

            if (IsAzure(provider))
            {
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    throw new ArgumentException(
                        "Azure OpenAI requiere un endpoint configurado en Integraciones.",
                        nameof(endpoint));
                }

                builder.AddAzureOpenAIChatCompletion(
                    deploymentName: effectiveModelId,
                    endpoint: endpoint,
                    apiKey: apiKey,
                    httpClient: _httpClient);
            }
            else
            {
                if (!OpenAICompatibleEndpointValidator.TryValidate(endpoint, out var customEndpoint, out var error))
                {
                    // Never include the API key here: this message ends up in AiEngineException.InnerException.
                    throw new ArgumentException(error, nameof(endpoint));
                }

                if (customEndpoint is null)
                {
                    // Official OpenAI endpoint (https://api.openai.com/v1).
                    builder.AddOpenAIChatCompletion(
                        modelId: effectiveModelId,
                        apiKey: apiKey,
                        orgId: null,
                        httpClient: _httpClient);
                }
                else
                {
                    // OpenAI-compatible endpoint (Groq, local models, ...). SK 1.77 overload:
                    // AddOpenAIChatCompletion(string modelId, Uri endpoint, string? apiKey, string? orgId = null,
                    //                         string? serviceId = null, HttpClient? httpClient = null)
                    builder.AddOpenAIChatCompletion(
                        modelId: effectiveModelId,
                        endpoint: customEndpoint,
                        apiKey: apiKey,
                        orgId: null,
                        httpClient: _httpClient);
                }
            }

            var kernel = builder.Build();
            return kernel.GetRequiredService<IChatCompletionService>();
        }

        private static bool IsAzure(string provider)
            => !string.IsNullOrWhiteSpace(provider)
               && (provider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase)
                   || provider.Contains("azure", StringComparison.OrdinalIgnoreCase));
    }
}
