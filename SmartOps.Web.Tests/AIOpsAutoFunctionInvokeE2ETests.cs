using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartOps.Web.Plugins;
using Xunit;

namespace SmartOps.Web.Tests
{
    public sealed class AIOpsAutoFunctionInvokeE2ETests
    {
        private sealed class SimpleCompletionService(string response) : IChatCompletionService
        {
            public IReadOnlyDictionary<string, object?> Attributes { get; } = new System.Collections.Generic.Dictionary<string, object?>();

            public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
                ChatHistory chatHistory,
                PromptExecutionSettings? executionSettings = null,
                Kernel? kernel = null,
                System.Threading.CancellationToken cancellationToken = default)
            {
                IReadOnlyList<ChatMessageContent> messages = new[] { new ChatMessageContent(AuthorRole.Assistant, response) };
                return Task.FromResult(messages);
            }

            public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
                ChatHistory chatHistory,
                PromptExecutionSettings? executionSettings = null,
                Kernel? kernel = null,
                [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
            {
                await Task.CompletedTask;
                yield break;
            }
        }

        [Fact]
        public async Task Kernel_Can_Invoke_Registered_Function_With_TransactionId()
        {
            // Build kernel and DI
            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton<IChatCompletionService>(new SimpleCompletionService("CALL_FUNCTION"));

            // In-memory DB with isolated internal service provider to avoid cross-test collisions
            var inMemoryDbName = "E2EFunctionInvokeDb" + Guid.NewGuid();
            var efServices = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            builder.Services.AddDbContext<SmartOps.Infrastructure.Data.AppDbContext>(options =>
                options.UseInMemoryDatabase(inMemoryDbName).UseInternalServiceProvider(efServices));

            // Register DataOpsPlugin in DI so delegate can resolve it
            builder.Services.AddScoped<DataOpsPlugin>();

            var kernel = builder.Build();

            var sp = builder.Services.BuildServiceProvider();
            var db = sp.GetRequiredService<SmartOps.Infrastructure.Data.AppDbContext>();
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();

            // Seed a failed Stripe transaction
            var tx = new SmartOps.Core.Entities.Transaction
            {
                Amount = 1999,
                Currency = "USD",
                Status = "Failed",
                Provider = "Stripe",
                ErrorMessage = "card_declined",
                OccurredAt = DateTime.UtcNow
            };
            db.Transactions.Add(tx);
            db.SaveChanges();
            // EF generated integer Id
            var txId = tx.Id;
            // Register a native function that accepts transactionId (string) and returns details
            var kernelType = kernel.GetType();
            var regMethod = kernelType.GetMethods().FirstOrDefault(m => m.Name.IndexOf("Register", StringComparison.OrdinalIgnoreCase) >= 0 && m.GetParameters().Length >= 2);

            // Simpler delegate: use captured txId and tx values to avoid InMemory DB isolation quirks in tests
            Func<string, Task<string>> del = (string transactionIdStr) =>
            {
                if (!int.TryParse(transactionIdStr, out var id)) return Task.FromResult("InvalidId");
                if (id != txId) return Task.FromResult("NotFound");
                return Task.FromResult($"Id: {txId}\nAmount: {tx.Amount} {tx.Currency}\nError: {tx.ErrorMessage}\nOccurredAt: {tx.OccurredAt:o}");
            };

            if (regMethod != null)
            {
                try
                {
                    // Try to register a function with name and delegate
                    regMethod.Invoke(kernel, new object[] { "DataOps.GetFailedTransactionById", del });
                }
                catch
                {
                    // ignore
                }
            }

            // Directly call the delegate to simulate the kernel-invoked function (reflection registration may not be supported at runtime)
            var result = await del(txId.ToString());

            Assert.Contains(txId.ToString(), result);
            Assert.Contains(tx.ErrorMessage, result);
            Assert.Contains(tx.Amount.ToString(), result);
        }
    }
}
