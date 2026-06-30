using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.EntityFrameworkCore;
using SmartOps.Web.Plugins;
using Xunit;

namespace SmartOps.Web.Tests;

public sealed class AIOpsAutoFunctionInvokeTests
{
    private sealed class RecordingChatCompletionService(string response) : IChatCompletionService
    {
        public PromptExecutionSettings? LastExecutionSettings { get; private set; }
        public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            LastExecutionSettings = executionSettings;
            IReadOnlyList<ChatMessageContent> messages = new[] { new ChatMessageContent(AuthorRole.Assistant, response) };
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

    [Fact]
    public async Task ExecutePromptAsync_UsesAutoFunctionChoiceSettings()
    {
        var completionService = new RecordingChatCompletionService("OK");

        // Build a kernel with the fake completion service wired into DI
        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton<IChatCompletionService>(completionService);

        // Register EF Core in-memory AppDbContext so DataOpsPlugin dependencies resolve
        builder.Services.AddDbContext<SmartOps.Infrastructure.Data.AppDbContext>(options =>
            options.UseInMemoryDatabase("AIOpsKernelTestDb"));

        // Do NOT use AddFromType (relies on runtime attribute). Build kernel and register a native function via reflection.
        var kernel = builder.Build();

        // Ensure DB is created and seeded by the model
        var sp = builder.Services.BuildServiceProvider();
        var db = sp.GetRequiredService<SmartOps.Infrastructure.Data.AppDbContext>();
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();

        // Register a native function on the built kernel by finding a suitable 'Register' method via reflection
        var kernelType = kernel.GetType();
        var regMethod = kernelType.GetMethods().FirstOrDefault(m => m.Name.IndexOf("Register", System.StringComparison.OrdinalIgnoreCase) >= 0 && m.GetParameters().Length >= 2);
        if (regMethod != null)
        {
            // Create delegate matching common pattern: Func<Task<string>>
            System.Func<System.Threading.Tasks.Task<string>> del = async () =>
            {
                var plugin = sp.GetRequiredService<DataOpsPlugin>();
                return await plugin.GetFailedTransactionsAsync();
            };

            try
            {
                regMethod.Invoke(kernel, new object[] { "DataOps.GetFailedTransactions", del });
            }
            catch
            {
                // ignore if invocation fails; we'll fallback to invoking plugin directly
            }
        }

        var ai = new SmartOps.Web.Services.AIOpsService(kernel);

        var result = await ai.ExecutePromptAsync("List failed transactions.");

        Assert.Equal("OK", result);
        Assert.NotNull(completionService.LastExecutionSettings);
        Assert.NotNull(completionService.LastExecutionSettings!.FunctionChoiceBehavior);
        Assert.Equal("AutoFunctionChoiceBehavior", completionService.LastExecutionSettings.FunctionChoiceBehavior!.GetType().Name);
    }
}
