using System;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace SmartOps.Web.Services
{
    /// <summary>
    /// Production implementation of <see cref="IChatCompletionServiceFactory"/>. Builds a real
    /// Semantic Kernel connector (OpenAI or Azure OpenAI) from the credentials persisted in /integrations.
    /// </summary>
    public sealed class SemanticKernelChatCompletionServiceFactory : IChatCompletionServiceFactory
    {
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
                        "Azure OpenAI requiere un endpoint configurado (Integrations.Endpoint o AI:Endpoint / AZURE_OPENAI_ENDPOINT).",
                        nameof(endpoint));
                }

                builder.AddAzureOpenAIChatCompletion(deploymentName: effectiveModelId, endpoint: endpoint, apiKey: apiKey);
            }
            else
            {
                builder.AddOpenAIChatCompletion(modelId: effectiveModelId, apiKey: apiKey, orgId: null);
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
