using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SmartOps.Core.Entities;
using SmartOps.Infrastructure.Data;
using SmartOps.Web.Services;
using Xunit;

namespace SmartOps.Web.Tests
{
    public sealed class AzureOpenAiDiagnosticEngineTests : IDisposable
    {
        private readonly DbConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;

        public AzureOpenAiDiagnosticEngineTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;

            using var db = new AppDbContext(_options);
            db.Database.EnsureCreated();
            db.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT);");
        }

        private AppDbContext CreateDbWithSettings(string provider, string apiKey, string modelId = "gpt-4o")
        {
            var db = new AppDbContext(_options);
            db.Database.ExecuteSqlRaw(
                "INSERT OR REPLACE INTO Settings (Key,Value) VALUES ('Integrations.Provider', {0});", provider);
            db.Database.ExecuteSqlRaw(
                "INSERT OR REPLACE INTO Settings (Key,Value) VALUES ('Integrations.ApiKey', {0});", apiKey);
            db.Database.ExecuteSqlRaw(
                "INSERT OR REPLACE INTO Settings (Key,Value) VALUES ('Integrations.ModelId', {0});", modelId);
            return db;
        }

        private static Transaction SampleTransaction() => new()
        {
            Id = 1,
            CustomerId = 1,
            Amount = 49.00m,
            Currency = "USD",
            Status = "Failed",
            GatewayReference = "TXN_ERR_502",
            CardLast4 = "4242",
            Provider = "Stripe",
            ErrorMessage = "Card declined",
            OccurredAt = DateTime.UtcNow
        };

        [Fact]
        public async Task AnalyzeAsync_SuccessResponse_IsReturnedAsIs()
        {
            using var db = CreateDbWithSettings("OpenAI", "sk-valid-key");
            var expected = "## 🔍 Razón del fallo\nEmisor rechazó la tarjeta\n\n## 🛠️ Acción recomendada para el operador\n- Contactar al banco.\n\n## 📨 Mensaje sugerido para el cliente\nEstimado cliente...";
            var factory = new FakeChatCompletionServiceFactory(new SucceedingChatCompletionService(expected));
            var engine = new AzureOpenAiDiagnosticEngine(factory, db);

            var result = await engine.AnalyzeAsync(SampleTransaction());

            Assert.Equal(expected, result);
            Assert.Equal("OpenAI", factory.LastProvider);
            Assert.Equal("sk-valid-key", factory.LastApiKey);
            Assert.Equal("gpt-4o", factory.LastModelId);
        }

        [Fact]
        public async Task AnalyzeAsync_AuthenticationFailure_ThrowsClearAiEngineException()
        {
            using var db = CreateDbWithSettings("OpenAI", "sk-invalid-key");
            var factory = new FakeChatCompletionServiceFactory(
                new ThrowingChatCompletionService(new HttpOperationException("Unauthorized", null)
                {
                    StatusCode = System.Net.HttpStatusCode.Unauthorized
                }));
            var engine = new AzureOpenAiDiagnosticEngine(factory, db);

            var ex = await Assert.ThrowsAsync<AiEngineException>(() => engine.AnalyzeAsync(SampleTransaction()));

            Assert.Contains("API Key", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.IsType<HttpOperationException>(ex.InnerException);
        }

        [Fact]
        public async Task AnalyzeAsync_TimeoutOrGenericSdkError_ThrowsClearAiEngineException_WithoutCrashing()
        {
            using var db = CreateDbWithSettings("OpenAI", "sk-valid-key");
            var factory = new FakeChatCompletionServiceFactory(
                new ThrowingChatCompletionService(new TaskCanceledException("The operation timed out.")));
            var engine = new AzureOpenAiDiagnosticEngine(factory, db);

            var ex = await Assert.ThrowsAsync<AiEngineException>(() => engine.AnalyzeAsync(SampleTransaction()));

            Assert.Contains("tiempo de espera", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.IsType<TaskCanceledException>(ex.InnerException);
        }

        [Fact]
        public async Task AnalyzeAsync_MissingApiKey_ThrowsClearAiEngineException_WithoutCallingSdk()
        {
            using var db = CreateDbWithSettings("OpenAI", apiKey: string.Empty);
            var factory = new FakeChatCompletionServiceFactory(new SucceedingChatCompletionService("should not be used"));
            var engine = new AzureOpenAiDiagnosticEngine(factory, db);

            var ex = await Assert.ThrowsAsync<AiEngineException>(() => engine.AnalyzeAsync(SampleTransaction()));

            Assert.Contains("API Key", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(factory.WasCalled);
        }

        public void Dispose() => _connection?.Dispose();

        private sealed class FakeChatCompletionServiceFactory : IChatCompletionServiceFactory
        {
            private readonly IChatCompletionService _service;

            public FakeChatCompletionServiceFactory(IChatCompletionService service) => _service = service;

            public bool WasCalled { get; private set; }
            public string? LastProvider { get; private set; }
            public string? LastApiKey { get; private set; }
            public string? LastModelId { get; private set; }

            public IChatCompletionService Create(string provider, string apiKey, string modelId, string? endpoint)
            {
                WasCalled = true;
                LastProvider = provider;
                LastApiKey = apiKey;
                LastModelId = modelId;
                return _service;
            }
        }

        private sealed class SucceedingChatCompletionService(string response) : IChatCompletionService
        {
            public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

            public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
                ChatHistory chatHistory,
                PromptExecutionSettings? executionSettings = null,
                Kernel? kernel = null,
                CancellationToken cancellationToken = default)
            {
                IReadOnlyList<ChatMessageContent> messages = [new(AuthorRole.Assistant, response)];
                return Task.FromResult(messages);
            }

            public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
                ChatHistory chatHistory,
                PromptExecutionSettings? executionSettings = null,
                Kernel? kernel = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await Task.CompletedTask;
                yield break;
            }
        }

        private sealed class ThrowingChatCompletionService(Exception toThrow) : IChatCompletionService
        {
            public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

            public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
                ChatHistory chatHistory,
                PromptExecutionSettings? executionSettings = null,
                Kernel? kernel = null,
                CancellationToken cancellationToken = default)
            {
                throw toThrow;
            }

            public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
                ChatHistory chatHistory,
                PromptExecutionSettings? executionSettings = null,
                Kernel? kernel = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await Task.CompletedTask;
                throw toThrow;
#pragma warning disable CS0162 // Unreachable code detected: required so the compiler treats this as an iterator.
                yield break;
#pragma warning restore CS0162
            }
        }
    }
}
