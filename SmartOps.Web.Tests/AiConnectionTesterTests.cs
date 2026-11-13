using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using SmartOps.Web.Services;
using Xunit;
using FakeHandler = SmartOps.Web.Tests.OpenAICompatibleEndpointTests.FakeHandler;

namespace SmartOps.Web.Tests
{
    /// <summary>«Probar conexión»: real factory + fake HTTP handler, no network.</summary>
    public sealed class AiConnectionTesterTests
    {
        private const string ApiKey = "gsk-conn-test-SECRET-1234567890";
        private const string Groq = "https://api.groq.com/openai/v1";
        private const string Model = "openai/gpt-oss-20b";

        private const string ModelNotFoundBody =
            "{\"error\":{\"message\":\"The model `nope` does not exist or you do not have access to it.\",\"type\":\"invalid_request_error\",\"code\":\"model_not_found\"}}";

        private static AiConnectionTester Tester(FakeHandler handler, TimeSpan? timeout = null)
            => new(new SemanticKernelChatCompletionServiceFactory(new HttpClient(handler)), null, timeout ?? AiConnectionTester.DefaultTimeout);

        private static HttpResponseMessage Json(HttpStatusCode status, string json)
            => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        [Fact]
        public async Task Ok_ReturnsSuccessWithModel_AndSendsSmallTokenBudget()
        {
            var handler = FakeHandler.Success("pong");
            var result = await Tester(handler).TestAsync("OpenAI", ApiKey, Model, Groq);

            Assert.True(result.Ok);
            Assert.Equal($"Conexión correcta con {Model}", result.Message);
            var request = Assert.Single(handler.Requests);
            Assert.Equal("api.groq.com", request.Uri.Host);
            var body = request.Body;
            var tokens = body.TryGetProperty("max_completion_tokens", out var t) ? t : body.GetProperty("max_tokens");
            Assert.Equal(AiConnectionTester.MaxTokens, tokens.GetInt32());
        }

        [Fact]
        public async Task Ok_EvenWithEmptyContent_ReasoningModels()
        {
            var result = await Tester(FakeHandler.Success(string.Empty)).TestAsync("OpenAI", ApiKey, Model, Groq);
            Assert.True(result.Ok);
        }

        [Fact]
        public async Task Unauthorized_MapsToCredentialsMessage_WithoutKey()
        {
            var result = await Tester(FakeHandler.Status(HttpStatusCode.Unauthorized)).TestAsync("OpenAI", ApiKey, Model, Groq);

            Assert.False(result.Ok);
            Assert.Equal(AiEngineErrorMapper.UnauthorizedMessage, result.Message);
            Assert.DoesNotContain(ApiKey, result.Message);
        }

        [Fact]
        public async Task NotFound_ModelNotFound_MapsToModelMessage()
        {
            var handler = new FakeHandler(_ => Json(HttpStatusCode.NotFound, ModelNotFoundBody));
            var result = await Tester(handler).TestAsync("OpenAI", ApiKey, "nope", Groq);

            Assert.False(result.Ok);
            Assert.Equal("El modelo «nope» no existe o esta key no tiene acceso. Verificá el ID del modelo.", result.Message);
        }

        [Fact]
        public async Task NotFound_WithoutModelNotFoundCode_KeepsGenericMessage()
        {
            var result = await Tester(FakeHandler.Status(HttpStatusCode.NotFound)).TestAsync("OpenAI", ApiKey, Model, Groq);
            Assert.False(result.Ok);
            Assert.Contains("(404)", result.Message);
        }

        [Fact]
        public async Task Timeout_ReturnsTimeoutMessage()
        {
            var slow = new SlowHandler();
            var tester = new AiConnectionTester(new SemanticKernelChatCompletionServiceFactory(new HttpClient(slow)), null, TimeSpan.FromMilliseconds(200));

            var result = await tester.TestAsync("OpenAI", ApiKey, Model, Groq);

            Assert.False(result.Ok);
            Assert.Equal(AiEngineErrorMapper.TimeoutMessage, result.Message);
        }

        [Fact]
        public async Task KeyNeverAppears_EvenWhenProviderEchoesIt()
        {
            var echo = new FakeHandler(_ => Json(HttpStatusCode.BadRequest,
                "{\"error\":{\"message\":\"Invalid key " + ApiKey + "\",\"type\":\"invalid_request_error\"}}"));
            var unexpected = new ThrowingFactory(new InvalidOperationException("boom with " + ApiKey));

            var r1 = await Tester(echo).TestAsync("OpenAI", ApiKey, Model, Groq);
            var r2 = await new AiConnectionTester(unexpected).TestAsync("OpenAI", ApiKey, Model, Groq);

            Assert.False(r1.Ok);
            Assert.False(r2.Ok);
            Assert.DoesNotContain(ApiKey, r1.Message);
            Assert.DoesNotContain(ApiKey, r2.Message);
        }

        [Fact]
        public async Task InvalidEndpoint_ReturnsValidatorMessage_WithoutNetwork()
        {
            var handler = FakeHandler.Success("unused");
            var result = await Tester(handler).TestAsync("OpenAI", ApiKey, Model, "http://api.groq.com/openai/v1");

            Assert.False(result.Ok);
            Assert.Equal(OpenAICompatibleEndpointValidator.HttpNotAllowedMessage, result.Message);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task MissingKey_DoesNotCallProvider()
        {
            var handler = FakeHandler.Success("unused");
            var result = await Tester(handler).TestAsync("OpenAI", " ", Model, Groq);
            Assert.False(result.Ok);
            Assert.Empty(handler.Requests);
        }

        private sealed class SlowHandler : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new InvalidOperationException("unreachable");
            }
        }

        private sealed class ThrowingFactory(Exception ex) : IChatCompletionServiceFactory
        {
            public Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService Create(string provider, string apiKey, string modelId, string? endpoint)
                => new AIOpsServiceErrorHandlingTests.ThrowingChatCompletionService(ex);
        }
    }
}
