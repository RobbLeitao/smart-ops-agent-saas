using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartOps.Infrastructure.Data;
using SmartOps.Web.Services;
using Xunit;

namespace SmartOps.Web.Tests
{
    /// <summary>
    /// /integrations (docs/redesign/05-integraciones.html): providers as radios, per-provider credentials,
    /// inline validation, «Probar conexión» and «Guardar cambios».
    /// </summary>
    public sealed class IntegrationsEndpointUiTests : IDisposable
    {
        private const string Groq = "https://api.groq.com/openai/v1";

        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"smartops-integrations-{Guid.NewGuid():N}.db");
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly BunitContext _ctx = new();
        private readonly FakeTester _tester = new();

        public IntegrationsEndpointUiTests()
        {
            _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
            using (var db = new AppDbContext(_options))
            {
                db.Database.EnsureCreated();
                db.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT);");
            }

            _ctx.Services.AddScoped(_ => new AppDbContext(_options));
            _ctx.Services.AddScoped<IntegrationSettingsStore>();
            _ctx.Services.AddSingleton<IAiConnectionTester>(_tester);
        }

        public void Dispose()
        {
            _ctx.Dispose();
            SqliteConnection.ClearAllPools();
            try { File.Delete(_dbPath); } catch (IOException) { }
        }

        private IRenderedComponent<SmartOps.Web.Components.Pages.Integrations> Render()
            => _ctx.Render<SmartOps.Web.Components.Pages.Integrations>();

        private static void Select(IRenderedComponent<SmartOps.Web.Components.Pages.Integrations> cut, string provider)
            => cut.Find($"input[type=radio][value={provider}]").Change(true);

        private static void Fill(IRenderedComponent<SmartOps.Web.Components.Pages.Integrations> cut, string key, string model, string endpoint)
        {
            cut.Find("#intg-apikey").Change(key);
            cut.Find("#intg-model").Change(model);
            cut.Find("#intg-endpoint").Change(endpoint);
        }

        private static void Save(IRenderedComponent<SmartOps.Web.Components.Pages.Integrations> cut) => cut.Find("form").Submit();

        private void Seed(string key, string value)
        {
            using var db = new AppDbContext(_options);
            db.Database.ExecuteSqlRaw("INSERT OR REPLACE INTO Settings (Key,Value) VALUES ({0},{1});", key, value);
        }

        private string? ReadSetting(string key)
        {
            using var db = new AppDbContext(_options);
            var conn = db.Database.GetDbConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Value FROM Settings WHERE Key = @k";
            var p = cmd.CreateParameter(); p.ParameterName = "@k"; p.Value = key; cmd.Parameters.Add(p);
            return cmd.ExecuteScalar() as string;
        }

        [Fact]
        public void Renders_TitleSubtitle_ProvidersFieldset_AndNoLegacyOrchestrationBlock()
        {
            var cut = Render();

            Assert.Equal("Integraciones", cut.Find("h1").TextContent);
            Assert.Contains("Elegí qué motor genera los diagnósticos. Los cambios se aplican en el próximo diagnóstico.", cut.Markup);
            Assert.Equal("Proveedor de IA", cut.Find("fieldset legend").TextContent);
            Assert.Equal(3, cut.FindAll("fieldset input[type=radio][name=provider]").Count);
            Assert.Contains("Orquestado con Microsoft Semantic Kernel.", cut.Markup);
            Assert.DoesNotContain("Motor principal", cut.Markup);
            Assert.DoesNotContain("Semantic Kernel - Activo", cut.Markup);
            Assert.Contains("Solo los Auditores ven y editan esta configuración.", cut.Markup);
        }

        [Fact]
        public void Local_ShowsNoFields_AndSavesProvider()
        {
            var cut = Render();

            Assert.Contains("El simulador no necesita credenciales.", cut.Markup);
            Assert.Empty(cut.FindAll("#intg-apikey"));
            Assert.Empty(cut.FindAll(".intg-test"));
            Assert.Contains("is-selected", cut.Find("label[data-provider=Local]").ClassName);

            Save(cut);

            cut.WaitForAssertion(() => Assert.Contains("Configuración guardada", cut.Markup));
            Assert.Equal("Local", ReadSetting("Integrations.Provider"));
        }

        [Fact]
        public void OpenAI_ApiKeyHiddenByDefault_MostrarTogglesIt()
        {
            var cut = Render();
            Select(cut, "OpenAI");

            Assert.Equal("Credenciales de OpenAI o compatible", cut.Find("#cred-title").TextContent);
            Assert.Equal("password", cut.Find("#intg-apikey").GetAttribute("type"));
            Assert.Contains("Nunca aparece en logs ni en mensajes de error.", cut.Markup);
            Assert.Contains("Vacío usa OpenAI. Para Groq:", cut.Markup);
            Assert.Contains("(opcional)", cut.Markup);

            cut.Find(".ds-reveal-btn").Click();
            Assert.Equal("text", cut.Find("#intg-apikey").GetAttribute("type"));
            Assert.Equal("Ocultar", cut.Find(".ds-reveal-btn").TextContent);
        }

        [Fact]
        public void SwitchingProvider_ShowsEachProvidersOwnValues_NeverTheOthers()
        {
            Seed("Integrations.Provider", "OpenAI");
            Seed("Integrations.OpenAI.ApiKey", "gsk-openai");
            Seed("Integrations.OpenAI.ModelId", "openai/gpt-oss-20b");
            Seed("Integrations.OpenAI.Endpoint", Groq);

            var cut = Render();
            Assert.Equal(Groq, cut.Find("#intg-endpoint").GetAttribute("value"));

            Select(cut, "AzureOpenAI");
            Assert.Equal("Credenciales de Azure OpenAI", cut.Find("#cred-title").TextContent);
            Assert.Equal(string.Empty, cut.Find("#intg-apikey").GetAttribute("value") ?? string.Empty);
            Assert.Equal(string.Empty, cut.Find("#intg-endpoint").GetAttribute("value") ?? string.Empty);

            // Unsaved Azure draft survives a round trip, and OpenAI's values come back intact.
            cut.Find("#intg-endpoint").Change("https://contoso.openai.azure.com/");
            Select(cut, "OpenAI");
            Assert.Equal(Groq, cut.Find("#intg-endpoint").GetAttribute("value"));
            Assert.Equal("openai/gpt-oss-20b", cut.Find("#intg-model").GetAttribute("value"));
            Select(cut, "AzureOpenAI");
            Assert.Equal("https://contoso.openai.azure.com/", cut.Find("#intg-endpoint").GetAttribute("value"));
        }

        [Fact]
        public void SavingAzure_DoesNotTouchOpenAIValues()
        {
            Seed("Integrations.OpenAI.Endpoint", Groq);
            var cut = Render();
            Select(cut, "AzureOpenAI");
            Fill(cut, "azure-key", "my-deployment", "https://contoso.openai.azure.com/");
            Save(cut);

            cut.WaitForAssertion(() => Assert.Equal("AzureOpenAI", ReadSetting("Integrations.Provider")));
            Assert.Equal("https://contoso.openai.azure.com/", ReadSetting("Integrations.AzureOpenAI.Endpoint"));
            Assert.Equal("my-deployment", ReadSetting("Integrations.AzureOpenAI.ModelId"));
            Assert.Equal(Groq, ReadSetting("Integrations.OpenAI.Endpoint"));
        }

        [Theory]
        [InlineData(Groq)]
        [InlineData("http://localhost:11434/v1")]
        [InlineData("")]
        public void OpenAI_ValidEndpoint_IsSaved(string endpoint)
        {
            var cut = Render();
            Select(cut, "OpenAI");
            Fill(cut, "gsk-test-key", "llama-3.3-70b-versatile", endpoint);
            Save(cut);

            cut.WaitForAssertion(() => Assert.Equal("OpenAI", ReadSetting("Integrations.Provider")));
            Assert.Equal(endpoint, ReadSetting("Integrations.OpenAI.Endpoint"));
            Assert.Equal("llama-3.3-70b-versatile", ReadSetting("Integrations.OpenAI.ModelId"));
            Assert.Contains("Configuración guardada", cut.Markup);
        }

        [Theory]
        [InlineData("http://api.groq.com/openai/v1", OpenAICompatibleEndpointValidator.HttpNotAllowedMessage)]
        [InlineData("esto no es una url", OpenAICompatibleEndpointValidator.InvalidUriMessage)]
        public void OpenAI_InvalidEndpoint_ShowsInlineErrorAndDoesNotSave(string endpoint, string expectedError)
        {
            var cut = Render();
            Select(cut, "OpenAI");
            Fill(cut, "gsk-test-key", "m", endpoint);
            Save(cut);

            Assert.Equal(expectedError, cut.Find("#intg-endpoint-error").TextContent);
            Assert.Equal("true", cut.Find("#intg-endpoint").GetAttribute("aria-invalid"));
            Assert.Null(ReadSetting("Integrations.Provider"));
        }

        [Fact]
        public void Azure_RequiredFields_ShowInlineErrors()
        {
            var cut = Render();
            Select(cut, "AzureOpenAI");
            Save(cut);

            Assert.Equal("La API key es obligatoria.", cut.Find("#intg-apikey-error").TextContent);
            Assert.Equal("El deployment es obligatorio.", cut.Find("#intg-model-error").TextContent);
            Assert.Equal("El endpoint de Azure OpenAI es obligatorio.", cut.Find("#intg-endpoint-error").TextContent);
            Assert.Null(ReadSetting("Integrations.Provider"));
        }

        [Fact]
        public void ProbarConexion_Ok_ShowsSuccess_UsesFormValues_AndSavesNothing()
        {
            _tester.Result = new ConnectionTestResult(true, "Conexión correcta con openai/gpt-oss-20b");
            var cut = Render();
            Select(cut, "OpenAI");
            Fill(cut, "gsk-form", "openai/gpt-oss-20b", Groq);

            cut.Find(".intg-test").Click();

            cut.WaitForAssertion(() => Assert.Contains("Conexión correcta con", cut.Find(".ds-alert-success").TextContent));
            Assert.Equal("openai/gpt-oss-20b", cut.Find(".ds-alert-success .mono").TextContent);
            Assert.Equal(("OpenAI", "gsk-form", "openai/gpt-oss-20b", Groq), _tester.LastCall);
            Assert.Null(ReadSetting("Integrations.Provider"));
            Assert.Null(ReadSetting("Integrations.OpenAI.ApiKey"));
        }

        [Fact]
        public void ProbarConexion_Error_ShowsDangerAlert_AndDisablesWhileTesting()
        {
            var gate = new TaskCompletionSource<ConnectionTestResult>();
            _tester.Pending = gate.Task;
            var cut = Render();
            Select(cut, "OpenAI");
            Fill(cut, "gsk-form", "nope", Groq);

            cut.Find(".intg-test").Click();
            Assert.True(cut.Find(".intg-test").HasAttribute("disabled"));
            Assert.Equal("Probando…", cut.Find(".intg-test").TextContent.Trim());

            gate.SetResult(new ConnectionTestResult(false, AiEngineErrorMapper.ModelNotFoundMessage("nope")));

            cut.WaitForAssertion(() => Assert.Contains("no existe o esta key no tiene acceso", cut.Find(".ds-alert-danger").TextContent));
            Assert.False(cut.Find(".intg-test").HasAttribute("disabled"));
        }

        private sealed class FakeTester : IAiConnectionTester
        {
            public ConnectionTestResult Result { get; set; } = new(true, "ok");
            public Task<ConnectionTestResult>? Pending { get; set; }
            public (string, string, string, string?)? LastCall { get; private set; }

            public Task<ConnectionTestResult> TestAsync(string provider, string apiKey, string modelId, string? endpoint, CancellationToken cancellationToken = default)
            {
                LastCall = (provider, apiKey, modelId, endpoint);
                return Pending ?? Task.FromResult(Result);
            }
        }
    }
}
