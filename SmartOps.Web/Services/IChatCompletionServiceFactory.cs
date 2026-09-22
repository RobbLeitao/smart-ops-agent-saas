using Microsoft.SemanticKernel.ChatCompletion;

namespace SmartOps.Web.Services
{
    /// <summary>
    /// Builds a Semantic Kernel <see cref="IChatCompletionService"/> for a given provider/credentials
    /// combination. Extracted as an interface so unit tests can substitute a fake chat completion
    /// service instead of hitting a real OpenAI/Azure OpenAI endpoint.
    /// </summary>
    public interface IChatCompletionServiceFactory
    {
        /// <param name="provider">"OpenAI" or "AzureOpenAI" (case-insensitive).</param>
        /// <param name="apiKey">API key persisted in /integrations.</param>
        /// <param name="modelId">Model/deployment id persisted in /integrations (e.g. "gpt-4o").</param>
        /// <param name="endpoint">Azure OpenAI endpoint. Ignored for plain OpenAI.</param>
        IChatCompletionService Create(string provider, string apiKey, string modelId, string? endpoint);
    }
}
