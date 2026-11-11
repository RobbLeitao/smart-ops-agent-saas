using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SmartOps.Infrastructure.Data;
using SmartOps.Web.Services;
using Xunit;

namespace SmartOps.Web.Tests
{
    /// <summary>
    /// Provider selection and typed error handling of <see cref="AIOpsService"/> when a real provider
    /// (OpenAI / Azure OpenAI) is configured in /integrations. Ported from the former
    /// AzureOpenAiDiagnosticEngineTests (Path A), now exercised through the single AI engine.
    /// </summary>
    public sealed class AIOpsServiceErrorHandlingTests : IDisposable
    {
        private const string Prompt = "Analiza la transacción\nErrorMessage: Card declined\n";

        private readonly DbConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;

        public AIOpsServiceErrorHandlingTests()
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

        private AppDbContext CreateDbWithSettings(string provider, string apiKey, string modelId = "gpt-4o", string? endpoint = null)
        {
            var db = new AppDbContext(_options);
            db.Database.ExecuteSqlRaw(
                "INSERT OR REPLACE INTO Settings (Key,Value) VALUES ('Integrations.Provider', {0});", provider);
            db.Database.ExecuteSqlRaw(
                "INSERT OR REPLACE INTO Settings (Key,Value) VALUES ('Integrations.ApiKey', {0});", apiKey);
            db.Database.ExecuteSqlRaw(
                "INSERT OR REPLACE INTO Settings (Key,Value) VALUES ('Integrations.ModelId', {0});", modelId);
            if (endpoint != null)
            {
                db.Database.ExecuteSqlRaw(
                    "INSERT OR REPLACE INTO Settings (Key,Value) VALUES ('Integrations.Endpoint', {0});", endpoint);
            }
            return db;
        }

        // Application Kernel with its own (recording) chat service, to assert it is never used as a
        // silent fallback when a real provider is configured in /integrations.
        private static (Kernel Kernel, RecordingChatCompletionService Service) CreateDefaultKernel()
        {
            var service = new RecordingChatCompletionService("DEFAULT KERNEL RESPONSE");
            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton<IChatCompletionService>(service);
            return (builder.Build(), service);
        }

        [Fact]
        public async Task ExecutePromptAsync_SuccessResponse_IsReturnedAsIs()
        {
            using var db = CreateDbWithSettings("OpenAI", "sk-valid-key");
            var expected = "## 🔍 Razón del fallo\nEmisor rechazó la tarjeta\n\n## 🛠️ Acción recomendada para el operador\n- Contactar al banco.\n\n## 📨 Mensaje sugerido para el cliente\nEstimado cliente...";
            var factory = new FakeChatCompletionServiceFactory(new RecordingChatCompletionService(expected));
            var (kernel, defaultService) = CreateDefaultKernel();
            var service = new AIOpsService(kernel, db, factory);

            var result = await service.ExecutePromptAsync(Prompt);

            Assert.Equal(expected, result);
            Assert.Equal("OpenAI", factory.LastProvider);
            Assert.Equal("sk-valid-key", factory.LastApiKey);
            Assert.Equal("gpt-4o", factory.LastModelId);
            Assert.False(defaultService.WasCalled);
        }

        [Fact]
        public async Task ExecutePromptAsync_AuthenticationFailure_ThrowsClearAiEngineException()
        {
            using var db = CreateDbWithSettings("OpenAI", "sk-invalid-key");
            var factory = new FakeChatCompletionServiceFactory(
                new ThrowingChatCompletionService(new HttpOperationException("Unauthorized", null)
                {
                    StatusCode = System.Net.HttpStatusCode.Unauthorized
                }));
            var (kernel, defaultService) = CreateDefaultKernel();
            var service = new AIOpsService(kernel, db, factory);

            var ex = await Assert.ThrowsAsync<AiEngineException>(() => service.ExecutePromptAsync(Prompt));

            Assert.Contains("API Key", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.IsType<HttpOperationException>(ex.InnerException);
            // No silent fallback to the application Kernel / Local Simulator.
            Assert.False(defaultService.WasCalled);
        }

        [Fact]
        public async Task ExecutePromptAsync_TimeoutOrGenericSdkError_ThrowsClearAiEngineException_WithoutCrashing()
        {
            using var db = CreateDbWithSettings("OpenAI", "sk-valid-key");
            var factory = new FakeChatCompletionServiceFactory(
                new ThrowingChatCompletionService(new TaskCanceledException("The operation timed out.")));
            var (kernel, _) = CreateDefaultKernel();
            var service = new AIOpsService(kernel, db, factory);

            var ex = await Assert.ThrowsAsync<AiEngineException>(() => service.ExecutePromptAsync(Prompt));

            Assert.Contains("tiempo de espera", ex.Message, StringComparison.OrdinalIgnoreCase);
            // Through Kernel.InvokePromptAsync, SK wraps the cancellation in KernelFunctionCanceledException
            // (an OperationCanceledException) that keeps the original exception as its inner exception.
            var canceled = Assert.IsAssignableFrom<OperationCanceledException>(ex.InnerException);
            Assert.IsType<TaskCanceledException>(canceled.InnerException);
        }

        [Fact]
        public async Task ExecutePromptAsync_MissingApiKey_ThrowsClearAiEngineException_WithoutCallingSdk()
        {
            using var db = CreateDbWithSettings("OpenAI", apiKey: string.Empty);
            var factory = new FakeChatCompletionServiceFactory(new RecordingChatCompletionService("should not be used"));
            var (kernel, defaultService) = CreateDefaultKernel();
            var service = new AIOpsService(kernel, db, factory);

            var ex = await Assert.ThrowsAsync<AiEngineException>(() => service.ExecutePromptAsync(Prompt));

            Assert.Contains("API Key", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(factory.WasCalled);
            Assert.False(defaultService.WasCalled);
        }

        private static HttpOperationException HttpError(System.Net.HttpStatusCode status)
            => new($"HTTP {(int)status}", null) { StatusCode = status };

        [Fact]
        public async Task ExecutePromptAsync_Forbidden_ThrowsInvalidCredentialsMessage()
        {
            using var db = CreateDbWithSettings("OpenAI", "sk-no-permissions");
            var factory = new FakeChatCompletionServiceFactory(
                new ThrowingChatCompletionService(HttpError(System.Net.HttpStatusCode.Forbidden)));
            var service = new AIOpsService(CreateDefaultKernel().Kernel, db, factory);

            var ex = await Assert.ThrowsAsync<AiEngineException>(() => service.ExecutePromptAsync(Prompt));

            Assert.Contains("API Key", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("permisos", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ExecutePromptAsync_RateLimited_ThrowsRateLimitMessage()
        {
            using var db = CreateDbWithSettings("OpenAI", "sk-valid-key");
            var factory = new FakeChatCompletionServiceFactory(
                new ThrowingChatCompletionService(HttpError(System.Net.HttpStatusCode.TooManyRequests)));
            var service = new AIOpsService(CreateDefaultKernel().Kernel, db, factory);

            var ex = await Assert.ThrowsAsync<AiEngineException>(() => service.ExecutePromptAsync(Prompt));

            Assert.Contains("rate limit", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.IsType<HttpOperationException>(ex.InnerException);
        }

        [Theory]
        [InlineData(System.Net.HttpStatusCode.RequestTimeout)]
        [InlineData(System.Net.HttpStatusCode.GatewayTimeout)]
        public async Task ExecutePromptAsync_HttpTimeoutStatus_ThrowsTimeoutMessage(System.Net.HttpStatusCode status)
        {
            using var db = CreateDbWithSettings("OpenAI", "sk-valid-key");
            var factory = new FakeChatCompletionServiceFactory(new ThrowingChatCompletionService(HttpError(status)));
            var service = new AIOpsService(CreateDefaultKernel().Kernel, db, factory);

            var ex = await Assert.ThrowsAsync<AiEngineException>(() => service.ExecutePromptAsync(Prompt));

            Assert.Contains("tiempo de espera", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ExecutePromptAsync_OtherHttpError_IncludesStatusCode()
        {
            using var db = CreateDbWithSettings("OpenAI", "sk-valid-key");
            var factory = new FakeChatCompletionServiceFactory(
                new ThrowingChatCompletionService(HttpError(System.Net.HttpStatusCode.InternalServerError)));
            var service = new AIOpsService(CreateDefaultKernel().Kernel, db, factory);

            var ex = await Assert.ThrowsAsync<AiEngineException>(() => service.ExecutePromptAsync(Prompt));

            Assert.Contains("(500)", ex.Message);
        }

        [Fact]
        public async Task ExecutePromptAsync_GenericSdkError_ThrowsUnexpectedErrorMessage()
        {
            using var db = CreateDbWithSettings("OpenAI", "sk-valid-key");
            var factory = new FakeChatCompletionServiceFactory(
                new ThrowingChatCompletionService(new InvalidOperationException("boom")));
            var service = new AIOpsService(CreateDefaultKernel().Kernel, db, factory);

            var ex = await Assert.ThrowsAsync<AiEngineException>(() => service.ExecutePromptAsync(Prompt));

            Assert.Contains("error inesperado", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("boom", ex.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task ExecutePromptAsync_EmptyResponse_ThrowsNoContentMessage(string emptyResponse)
        {
            using var db = CreateDbWithSettings("OpenAI", "sk-valid-key");
            var factory = new FakeChatCompletionServiceFactory(new RecordingChatCompletionService(emptyResponse));
            var service = new AIOpsService(CreateDefaultKernel().Kernel, db, factory);

            var ex = await Assert.ThrowsAsync<AiEngineException>(() => service.ExecutePromptAsync(Prompt));

            Assert.Contains("no devolvió contenido", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ExecutePromptAsync_ConnectorInitializationFails_ThrowsInitializationMessage()
        {
            using var db = CreateDbWithSettings("OpenAI", "sk-valid-key");
            var factory = new FakeChatCompletionServiceFactory(new ArgumentException("bad model"));
            var service = new AIOpsService(CreateDefaultKernel().Kernel, db, factory);

            var ex = await Assert.ThrowsAsync<AiEngineException>(() => service.ExecutePromptAsync(Prompt));

            Assert.Contains("No se pudo inicializar el conector", ex.Message);
            Assert.IsType<ArgumentException>(ex.InnerException);
        }

        [Fact]
        public async Task ExecutePromptAsync_AzureProvider_PassesEndpointAndDeploymentToFactory()
        {
            using var db = CreateDbWithSettings("AzureOpenAI", "azure-key", "my-deployment", "https://contoso.openai.azure.com/");
            var factory = new FakeChatCompletionServiceFactory(new RecordingChatCompletionService("AZURE RESPONSE"));
            var (kernel, defaultService) = CreateDefaultKernel();
            var service = new AIOpsService(kernel, db, factory);

            var result = await service.ExecutePromptAsync(Prompt);

            Assert.Equal("AZURE RESPONSE", result);
            Assert.Equal("AzureOpenAI", factory.LastProvider);
            Assert.Equal("azure-key", factory.LastApiKey);
            Assert.Equal("my-deployment", factory.LastModelId);
            Assert.Equal("https://contoso.openai.azure.com/", factory.LastEndpoint);
            Assert.False(defaultService.WasCalled);
        }

        [Fact]
        public async Task ExecutePromptAsync_AzureWithoutEndpoint_RealFactory_ThrowsInitializationMessage()
        {
            // Uses the production factory: it rejects Azure without endpoint before any network call.
            using var db = CreateDbWithSettings("AzureOpenAI", "azure-key", "my-deployment");
            var service = new AIOpsService(CreateDefaultKernel().Kernel, db, new SemanticKernelChatCompletionServiceFactory());

            var ex = await Assert.ThrowsAsync<AiEngineException>(() => service.ExecutePromptAsync(Prompt));

            Assert.Contains("endpoint", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.IsType<ArgumentException>(ex.InnerException);
        }

        [Fact]
        public async Task ExecutePromptAsync_LocalProvider_UsesDefaultKernel_AndIgnoresPersistedKey()
        {
            using var db = CreateDbWithSettings("Local", "sk-leftover-key");
            var factory = new FakeChatCompletionServiceFactory(new RecordingChatCompletionService("should not be used"));
            var (kernel, defaultService) = CreateDefaultKernel();
            var service = new AIOpsService(kernel, db, factory);

            var result = await service.ExecutePromptAsync(Prompt);

            Assert.Equal("DEFAULT KERNEL RESPONSE", result);
            Assert.True(defaultService.WasCalled);
            Assert.False(factory.WasCalled);
        }

        [Fact]
        public async Task ExecutePromptAsync_DefaultKernelProviderFails_ThrowsAiEngineException()
        {
            // Local mode with an environment-configured provider on the application Kernel that fails.
            using var db = CreateDbWithSettings("Local", string.Empty);
            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton<IChatCompletionService>(
                new ThrowingChatCompletionService(HttpError(System.Net.HttpStatusCode.Unauthorized)));
            var service = new AIOpsService(builder.Build(), db, new FakeChatCompletionServiceFactory(new RecordingChatCompletionService("unused")));

            var ex = await Assert.ThrowsAsync<AiEngineException>(() => service.ExecutePromptAsync(Prompt));

            Assert.Contains("API Key", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("Card declined", "Emisor rechazó la tarjeta")]
        [InlineData("Invalid CVC", "Código CVC inválido")]
        [InlineData("Insufficient funds", "Fondos insuficientes")]
        [InlineData("Suspected fraud", "Posible fraude detectado")]
        public async Task ExecutePromptAsync_LocalSimulator_ReturnsHeuristicDiagnosis(string errorMessage, string expectedReason)
        {
            // No db (unconfigured) and an empty Kernel: Local Simulator.
            var service = new AIOpsService(Kernel.CreateBuilder().Build());

            var result = await service.ExecutePromptAsync($"Contexto\nErrorMessage: {errorMessage}\n");

            Assert.Contains(expectedReason, result);
            Assert.Contains("## 📨 Mensaje sugerido para el cliente", result);
        }

        public void Dispose() => _connection?.Dispose();

        internal sealed class FakeChatCompletionServiceFactory : IChatCompletionServiceFactory
        {
            private readonly IChatCompletionService? _service;
            private readonly Exception? _createException;

            public FakeChatCompletionServiceFactory(IChatCompletionService service) => _service = service;

            public FakeChatCompletionServiceFactory(Exception createException) => _createException = createException;

            public bool WasCalled { get; private set; }
            public string? LastProvider { get; private set; }
            public string? LastApiKey { get; private set; }
            public string? LastModelId { get; private set; }
            public string? LastEndpoint { get; private set; }

            public IChatCompletionService Create(string provider, string apiKey, string modelId, string? endpoint)
            {
                WasCalled = true;
                LastProvider = provider;
                LastApiKey = apiKey;
                LastModelId = modelId;
                LastEndpoint = endpoint;
                if (_createException != null) throw _createException;
                return _service!;
            }
        }

        internal sealed class RecordingChatCompletionService(string response) : IChatCompletionService
        {
            public bool WasCalled { get; private set; }

            public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

            public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
                ChatHistory chatHistory,
                PromptExecutionSettings? executionSettings = null,
                Kernel? kernel = null,
                CancellationToken cancellationToken = default)
            {
                WasCalled = true;
                IReadOnlyList<ChatMessageContent> messages = [new(AuthorRole.Assistant, response)];
                return Task.FromResult(messages);
            }

            public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
                ChatHistory chatHistory,
                PromptExecutionSettings? executionSettings = null,
                Kernel? kernel = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                WasCalled = true;
                await Task.CompletedTask;
                yield break;
            }
        }

        internal sealed class ThrowingChatCompletionService(Exception toThrow) : IChatCompletionService
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
