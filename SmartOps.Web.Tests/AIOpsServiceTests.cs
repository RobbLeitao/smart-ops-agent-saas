using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SmartOps.Web.Services;
using Xunit;

namespace SmartOps.Web.Tests;

public sealed class AIOpsServiceTests
{
    [Fact]
    public async Task ExecutePromptAsync_ReturnsModelResponseAndEnablesAutoFunctionChoice()
    {
        var completionService = new RecordingChatCompletionService("SmartOps AI reply");
        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton<IChatCompletionService>(completionService);

        var service = new AIOpsService(builder.Build());

        var result = await service.ExecutePromptAsync("Summarize the current incidents.");

        Assert.Equal("SmartOps AI reply", result);
        Assert.NotNull(completionService.LastExecutionSettings);
        Assert.NotNull(completionService.LastExecutionSettings!.FunctionChoiceBehavior);
        Assert.Equal(
            "AutoFunctionChoiceBehavior",
            completionService.LastExecutionSettings.FunctionChoiceBehavior!.GetType().Name);
    }

    [Fact]
    public void ApplicationServices_CanResolveAIOpsService()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var scope = factory.Services.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<AIOpsService>();

        Assert.NotNull(service);
    }

    private sealed class RecordingChatCompletionService(string response) : IChatCompletionService
    {
        public PromptExecutionSettings? LastExecutionSettings { get; private set; }

        public IReadOnlyDictionary<string, object?> Attributes { get; } =
            new Dictionary<string, object?>();

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            LastExecutionSettings = executionSettings;

            IReadOnlyList<ChatMessageContent> messages =
            [
                new(AuthorRole.Assistant, response)
            ];

            return Task.FromResult(messages);
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastExecutionSettings = executionSettings;
            await Task.CompletedTask;
            yield break;
        }
    }
}
