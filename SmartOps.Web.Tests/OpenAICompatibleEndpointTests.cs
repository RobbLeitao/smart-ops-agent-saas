using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SmartOps.Infrastructure.Data;
using SmartOps.Web.Services;
using Xunit;

namespace SmartOps.Web.Tests
{
    /// <summary>
    /// Custom endpoint for the "OpenAI" provider (OpenAI-compatible APIs such as Groq). Uses the production
    /// <see cref="SemanticKernelChatCompletionServiceFactory"/> with a fake <see cref="HttpMessageHandler"/>:
    /// no real network calls are made.
    /// </summary>
    public sealed class OpenAICompatibleEndpointTests : IDisposable
    {
        private const string Prompt = "Analiza la transacción\nErrorMessage: Card declined\n";
        private const string ApiKey = "gsk-test-secret-key-123";
        private const string GroqEndpoint = "https://api.groq.com/openai/v1";

        private readonly DbConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;

        public OpenAICompatibleEndpointTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

            using var db = new AppDbContext(_options);
            db.Database.EnsureCreated();
            db.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT);");
        }

        public void Dispose() => _connection.Dispose();

        // ---------- Factory: endpoint selection ----------

        [Fact]
        public async Task Factory_OpenAI_WithCustomEndpoint_SendsRequestToThatEndpoint()
        {
            var handler = FakeHandler.Success("hola desde groq");
            var factory = new SemanticKernelChatCompletionServiceFactory(new HttpClient(handler));

            var chat = factory.Create("OpenAI", ApiKey, "llama-3.3-70b-versatile", GroqEndpoint);
            var result = await chat.GetChatMessageContentAsync("ping");

            Assert.Equal("hola desde groq", result.Content);
            var request = Assert.Single(handler.Requests);
            Assert.Equal("https://api.groq.com/openai/v1/chat/completions", request.Uri.ToString());
            Assert.Equal("Bearer " + ApiKey, request.Authorization);
            Assert.Equal("llama-3.3-70b-versatile", request.Body.GetProperty("model").GetString());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Factory_OpenAI_WithoutEndpoint_UsesOfficialOpenAIEndpoint(string? endpoint)
        {
            var handler = FakeHandler.Success("hola desde openai");
            var factory = new SemanticKernelChatCompletionServiceFactory(new HttpClient(handler));

            var chat = factory.Create("OpenAI", ApiKey, "gpt-4o", endpoint);
            await chat.GetChatMessageContentAsync("ping");

            var request = Assert.Single(handler.Requests);
            Assert.Equal("https://api.openai.com/v1/chat/completions", request.Uri.ToString());
        }

        [Fact]
        public async Task Factory_Azure_UsesAzureDeploymentUrl_AndApiKeyHeader()
        {
            var handler = FakeHandler.Success("hola desde azure");
            var factory = new SemanticKernelChatCompletionServiceFactory(new HttpClient(handler));

            var chat = factory.Create("AzureOpenAI", ApiKey, "my-deployment", "https://contoso.openai.azure.com/");
            var result = await chat.GetChatMessageContentAsync("ping");

            Assert.Equal("hola desde azure", result.Content);
            var request = Assert.Single(handler.Requests);
            Assert.Equal("contoso.openai.azure.com", request.Uri.Host);
            Assert.StartsWith("/openai/deployments/my-deployment/chat/completions", request.Uri.AbsolutePath);
            Assert.Equal(ApiKey, request.ApiKeyHeader);
            Assert.Null(request.Authorization);
        }

        [Fact]
        public void Factory_Azure_WithoutEndpoint_StillThrows()
        {
            var factory = new SemanticKernelChatCompletionServiceFactory();
            Assert.Throws<ArgumentException>(() => factory.Create("AzureOpenAI", ApiKey, "my-deployment", null));
        }

        [Theory]
        [InlineData("not a uri")]
        [InlineData("http://api.groq.com/openai/v1")]
        public void Factory_OpenAI_InvalidEndpoint_ThrowsArgumentException_WithoutLeakingKey(string endpoint)
        {
            var factory = new SemanticKernelChatCompletionServiceFactory();
            var ex = Assert.Throws<ArgumentException>(() => factory.Create("OpenAI", ApiKey, "gpt-4o", endpoint));
            Assert.DoesNotContain(ApiKey, ex.ToString());
        }

        [Fact]
        public async Task Factory_OpenAI_CustomEndpoint_DoesNotSendParametersGroqRejects()
        {
            var handler = FakeHandler.Success("ok");
            using var db = CreateDbWithSettings("OpenAI", ApiKey, "llama-3.3-70b-versatile", GroqEndpoint);
            var service = new AIOpsService(Kernel.CreateBuilder().Build(), db,
                new SemanticKernelChatCompletionServiceFactory(new HttpClient(handler)));

            await service.ExecutePromptAsync(Prompt);

            var body = Assert.Single(handler.Requests).Body;
            Assert.False(body.TryGetProperty("logprobs", out _));
            Assert.False(body.TryGetProperty("top_logprobs", out _));
            Assert.False(body.TryGetProperty("logit_bias", out _));
            foreach (var message in body.GetProperty("messages").EnumerateArray())
            {
                Assert.False(message.TryGetProperty("name", out _));
            }
        }

        // ---------- AIOpsService end-to-end with custom endpoint (fake HTTP) ----------

        [Fact]
        public async Task AIOpsService_CustomEndpoint_Success_ReturnsProviderContent()
        {
            var handler = FakeHandler.Success("## 🔍 Razón del fallo\nEmisor rechazó la tarjeta");
            using var db = CreateDbWithSettings("OpenAI", ApiKey, "llama-3.3-70b-versatile", GroqEndpoint);
            var service = new AIOpsService(Kernel.CreateBuilder().Build(), db,
                new SemanticKernelChatCompletionServiceFactory(new HttpClient(handler)));

            var result = await service.ExecutePromptAsync(Prompt);

            Assert.Contains("Emisor rechazó la tarjeta", result);
            Assert.Equal("api.groq.com", Assert.Single(handler.Requests).Uri.Host);
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized, "API Key")]
        [InlineData(HttpStatusCode.TooManyRequests, "rate limit")]
        [InlineData(HttpStatusCode.RequestTimeout, "tiempo de espera")]
        [InlineData(HttpStatusCode.GatewayTimeout, "tiempo de espera")]
        public async Task AIOpsService_CustomEndpoint_HttpErrors_MapToAiEngineException(HttpStatusCode status, string expected)
        {
            var handler = FakeHandler.Status(status);
            using var db = CreateDbWithSettings("OpenAI", ApiKey, "llama-3.3-70b-versatile", GroqEndpoint);
            var service = new AIOpsService(Kernel.CreateBuilder().Build(), db,
                new SemanticKernelChatCompletionServiceFactory(new HttpClient(handler)));

            var ex = await Assert.ThrowsAsync<AiEngineException>(() => service.ExecutePromptAsync(Prompt));

            Assert.Contains(expected, ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(ApiKey, ex.ToString());
            Assert.All(handler.Requests, r => Assert.Equal("api.groq.com", r.Uri.Host));
        }

        [Fact]
        public async Task AIOpsService_CustomEndpoint_NetworkTimeout_MapsToTimeoutAiEngineException()
        {
            var handler = new FakeHandler(_ => throw new TaskCanceledException("The request timed out."));
            using var db = CreateDbWithSettings("OpenAI", ApiKey, "llama-3.3-70b-versatile", GroqEndpoint);
            var service = new AIOpsService(Kernel.CreateBuilder().Build(), db,
                new SemanticKernelChatCompletionServiceFactory(new HttpClient(handler)));

            var ex = await Assert.ThrowsAsync<AiEngineException>(() => service.ExecutePromptAsync(Prompt));

            Assert.Contains("tiempo de espera", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(ApiKey, ex.ToString());
        }

        [Fact]
        public async Task AIOpsService_InvalidCustomEndpoint_ThrowsInitializationError_WithoutNetwork()
        {
            var handler = FakeHandler.Success("unused");
            using var db = CreateDbWithSettings("OpenAI", ApiKey, "gpt-4o", "http://api.groq.com/openai/v1");
            var service = new AIOpsService(Kernel.CreateBuilder().Build(), db,
                new SemanticKernelChatCompletionServiceFactory(new HttpClient(handler)));

            var ex = await Assert.ThrowsAsync<AiEngineException>(() => service.ExecutePromptAsync(Prompt));

            Assert.Contains("No se pudo inicializar el conector", ex.Message);
            Assert.DoesNotContain(ApiKey, ex.ToString());
            Assert.Empty(handler.Requests);
        }

        // ---------- Validator (shared with /integrations) ----------

        [Theory]
        [InlineData("https://api.groq.com/openai/v1")]
        [InlineData("https://api.openai.com/v1")]
        [InlineData("http://localhost:11434/v1")]
        [InlineData("http://127.0.0.1:1234/v1")]
        [InlineData("  https://api.groq.com/openai/v1  ")]
        public void Validator_AcceptsHttpsAndLocalHttp(string value)
        {
            Assert.True(OpenAICompatibleEndpointValidator.TryValidate(value, out var uri, out var error));
            Assert.NotNull(uri);
            Assert.Null(error);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Validator_EmptyIsValid_AndMeansOfficialEndpoint(string? value)
        {
            Assert.True(OpenAICompatibleEndpointValidator.TryValidate(value, out var uri, out var error));
            Assert.Null(uri);
            Assert.Null(error);
        }

        [Theory]
        [InlineData("http://api.groq.com/openai/v1")]
        [InlineData("http://192.168.1.10:8080/v1")]
        public void Validator_RejectsHttpToExternalHost(string value)
        {
            Assert.False(OpenAICompatibleEndpointValidator.TryValidate(value, out var uri, out var error));
            Assert.Null(uri);
            Assert.Equal(OpenAICompatibleEndpointValidator.HttpNotAllowedMessage, error);
        }

        [Theory]
        [InlineData("no es una url")]
        [InlineData("api.groq.com/openai/v1")]
        [InlineData("ftp://api.groq.com/openai/v1")]
        [InlineData("/openai/v1")]
        public void Validator_RejectsNonUri(string value)
        {
            Assert.False(OpenAICompatibleEndpointValidator.TryValidate(value, out var uri, out var error));
            Assert.Null(uri);
            Assert.Equal(OpenAICompatibleEndpointValidator.InvalidUriMessage, error);
        }

        // ---------- helpers ----------

        private AppDbContext CreateDbWithSettings(string provider, string apiKey, string modelId, string? endpoint)
        {
            var db = new AppDbContext(_options);
            void Set(string key, string value) => db.Database.ExecuteSqlRaw(
                "INSERT OR REPLACE INTO Settings (Key,Value) VALUES ({0}, {1});", key, value);
            Set("Integrations.Provider", provider);
            Set("Integrations.ApiKey", apiKey);
            Set("Integrations.ModelId", modelId);
            if (endpoint != null) Set("Integrations.Endpoint", endpoint);
            return db;
        }

        internal sealed record CapturedRequest(Uri Uri, string? Authorization, string? ApiKeyHeader, JsonElement Body);

        internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
        {
            public List<CapturedRequest> Requests { get; } = new();

            public static FakeHandler Success(string content) => new(_ => Json(HttpStatusCode.OK, ChatCompletionJson(content)));

            public static FakeHandler Status(HttpStatusCode status)
                => new(_ => Json(status, "{\"error\":{\"message\":\"fake error\",\"type\":\"fake\"}}"));

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var raw = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
                Requests.Add(new CapturedRequest(
                    request.RequestUri!,
                    request.Headers.Authorization?.ToString(),
                    request.Headers.TryGetValues("api-key", out var values) ? values.FirstOrDefault() : null,
                    JsonDocument.Parse(raw).RootElement.Clone()));
                return respond(request);
            }

            private static HttpResponseMessage Json(HttpStatusCode status, string json)
                => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

            private static string ChatCompletionJson(string content) => JsonSerializer.Serialize(new
            {
                id = "chatcmpl-test",
                @object = "chat.completion",
                created = 1700000000,
                model = "test-model",
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        message = new { role = "assistant", content },
                        finish_reason = "stop"
                    }
                },
                usage = new { prompt_tokens = 1, completion_tokens = 1, total_tokens = 2 }
            });
        }
    }
}
