using System;
using System.IO;
using System.Linq;
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
    /// /integrations: optional Endpoint for the OpenAI provider (OpenAI-compatible APIs such as Groq).
    /// Uses a file-backed SQLite DB because the page disposes the connection it gets from the DbContext.
    /// </summary>
    public sealed class IntegrationsEndpointUiTests : IDisposable
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"smartops-integrations-{Guid.NewGuid():N}.db");
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly TestContext _ctx = new();

        public IntegrationsEndpointUiTests()
        {
            _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
            using (var db = new AppDbContext(_options))
            {
                db.Database.EnsureCreated();
                db.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT);");
            }

            _ctx.Services.AddScoped(_ => new AppDbContext(_options));
        }

        public void Dispose()
        {
            _ctx.Dispose();
            SqliteConnection.ClearAllPools();
            try { File.Delete(_dbPath); } catch (IOException) { }
        }

        private IRenderedComponent<SmartOps.Web.Components.Pages.Integrations> RenderWithOpenAI(string endpoint)
        {
            var cut = _ctx.Render<SmartOps.Web.Components.Pages.Integrations>();
            cut.FindAll("button.integration-card")[1].Click(); // OpenAI API
            cut.FindAll("input.form-control")[0].Change("gsk-test-key");            // API Key
            cut.FindAll("input.form-control")[1].Change("llama-3.3-70b-versatile"); // Model ID
            cut.Find("input[placeholder='https://api.groq.com/openai/v1']").Change(endpoint);
            return cut;
        }

        private void ClickSave(IRenderedComponent<SmartOps.Web.Components.Pages.Integrations> cut)
            => cut.FindAll("button").Single(b => b.TextContent.Contains("Guardar Configuración")).Click();

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
        public void OpenAI_ShowsOptionalEndpointField_WithGroqHelp()
        {
            var cut = _ctx.Render<SmartOps.Web.Components.Pages.Integrations>();
            Assert.Empty(cut.FindAll("input[placeholder='https://api.groq.com/openai/v1']"));

            cut.FindAll("button.integration-card")[1].Click();

            Assert.Single(cut.FindAll("input[placeholder='https://api.groq.com/openai/v1']"));
            Assert.Contains("(opcional)", cut.Markup);
            Assert.Contains("Dejalo vacío para usar OpenAI", cut.Markup);
        }

        [Theory]
        [InlineData("https://api.groq.com/openai/v1")]
        [InlineData("http://localhost:11434/v1")]
        [InlineData("http://127.0.0.1:1234/v1")]
        public void OpenAI_ValidEndpoint_IsSaved(string endpoint)
        {
            var cut = RenderWithOpenAI(endpoint);
            ClickSave(cut);

            cut.WaitForAssertion(() => Assert.Equal(endpoint, ReadSetting("Integrations.Endpoint")));
            Assert.Equal("OpenAI", ReadSetting("Integrations.Provider"));
            Assert.Equal("llama-3.3-70b-versatile", ReadSetting("Integrations.ModelId"));
        }

        [Theory]
        [InlineData("http://api.groq.com/openai/v1", OpenAICompatibleEndpointValidator.HttpNotAllowedMessage)]
        [InlineData("esto no es una url", OpenAICompatibleEndpointValidator.InvalidUriMessage)]
        public void OpenAI_InvalidEndpoint_ShowsErrorAndDoesNotSave(string endpoint, string expectedError)
        {
            var cut = RenderWithOpenAI(endpoint);
            ClickSave(cut);

            Assert.Contains(expectedError, cut.Markup);
            Assert.Contains("is-invalid", cut.Find("input[placeholder='https://api.groq.com/openai/v1']").ClassName);
            Assert.Null(ReadSetting("Integrations.Provider"));
            Assert.Null(ReadSetting("Integrations.Endpoint"));
        }

        [Fact]
        public void OpenAI_ClearingEndpoint_PersistsEmpty_InsteadOfOldValue()
        {
            using (var db = new AppDbContext(_options))
            {
                db.Database.ExecuteSqlRaw("INSERT OR REPLACE INTO Settings (Key,Value) VALUES ('Integrations.Endpoint', 'https://api.groq.com/openai/v1');");
            }

            var cut = RenderWithOpenAI(string.Empty);
            ClickSave(cut);

            cut.WaitForAssertion(() => Assert.Equal("OpenAI", ReadSetting("Integrations.Provider")));
            Assert.Equal(string.Empty, ReadSetting("Integrations.Endpoint"));
        }

        [Fact]
        public void Azure_EndpointStillRequired()
        {
            var cut = _ctx.Render<SmartOps.Web.Components.Pages.Integrations>();
            cut.FindAll("button.integration-card")[2].Click(); // Azure OpenAI
            cut.FindAll("input.form-control")[0].Change("azure-key");
            cut.FindAll("input.form-control")[1].Change("my-deployment");
            ClickSave(cut);

            Assert.Contains("El endpoint de Azure OpenAI es obligatorio.", cut.Markup);
            Assert.Null(ReadSetting("Integrations.Provider"));
        }
    }
}
