using System;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartOps.Infrastructure.Data;
using SmartOps.Web.Services;
using Xunit;

namespace SmartOps.Web.Tests
{
    /// <summary>Per-provider integrations settings (IntegrationSettingsStore) and the legacy-keys migration.</summary>
    public sealed class IntegrationsSettingsPersistenceTests : IDisposable
    {
        private readonly DbConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;

        public IntegrationsSettingsPersistenceTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

            using var db = new AppDbContext(_options);
            db.Database.EnsureCreated();
            db.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT);");
        }

        public void Dispose() => _connection.Dispose();

        private IntegrationSettingsStore Store() => new(new AppDbContext(_options));

        private void Set(string key, string value)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO Settings (Key,Value) VALUES (@k,@v);";
            var k = cmd.CreateParameter(); k.ParameterName = "@k"; k.Value = key; cmd.Parameters.Add(k);
            var v = cmd.CreateParameter(); v.ParameterName = "@v"; v.Value = value; cmd.Parameters.Add(v);
            cmd.ExecuteNonQuery();
        }

        private string? Get(string key)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Value FROM Settings WHERE Key = @k";
            var k = cmd.CreateParameter(); k.ParameterName = "@k"; k.Value = key; cmd.Parameters.Add(k);
            return cmd.ExecuteScalar() as string;
        }

        private long CountLegacy()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Settings WHERE Key IN ('Integrations.ApiKey','Integrations.ModelId','Integrations.Endpoint')";
            return (long)cmd.ExecuteScalar()!;
        }

        [Fact]
        public void Save_ThenRead_EachProviderKeepsItsOwnValues()
        {
            Store().Save("OpenAI", new ProviderCredentials("gsk-test", "openai/gpt-oss-20b", "https://api.groq.com/openai/v1"));
            Store().Save("AzureOpenAI", new ProviderCredentials("azure-test", "my-deployment", "https://contoso.openai.azure.com/"));

            var openAi = Store().Read("OpenAI");
            var azure = Store().Read("AzureOpenAI");

            Assert.Equal(new ProviderCredentials("gsk-test", "openai/gpt-oss-20b", "https://api.groq.com/openai/v1"), openAi);
            Assert.Equal(new ProviderCredentials("azure-test", "my-deployment", "https://contoso.openai.azure.com/"), azure);
            Assert.Equal("AzureOpenAI", Store().ReadActiveProvider());
            Assert.Equal("azure-test", Store().ReadActive().Credentials.ApiKey);
        }

        [Fact]
        public void SaveLocal_KeepsRealProvidersCredentials()
        {
            Store().Save("OpenAI", new ProviderCredentials("gsk-test", "m", ""));
            Store().Save("Local", null);

            Assert.Equal("Local", Get("Integrations.Provider"));
            Assert.Equal("gsk-test", Store().Read("OpenAI").ApiKey);
            Assert.Equal(ProviderCredentials.Empty, Store().ReadActive().Credentials);
        }

        [Fact]
        public void ReadActive_NothingConfigured_ProviderIsNull()
        {
            var active = Store().ReadActive();
            Assert.Null(active.Provider);
            Assert.Equal(ProviderCredentials.Empty, active.Credentials);
        }

        [Fact]
        public void MigrateLegacy_MovesValuesToActiveProvider_DeletesLegacy_AndIsIdempotent()
        {
            Set("Integrations.Provider", "OpenAI");
            Set("Integrations.ApiKey", "gsk-legacy");
            Set("Integrations.ModelId", "openai/gpt-oss-20b");
            Set("Integrations.Endpoint", "https://api.groq.com/openai/v1");

            Assert.Equal(3, IntegrationSettingsStore.MigrateLegacy(_connection));

            Assert.Equal(0, CountLegacy());
            Assert.Equal("gsk-legacy", Get("Integrations.OpenAI.ApiKey"));
            Assert.Equal("openai/gpt-oss-20b", Get("Integrations.OpenAI.ModelId"));
            Assert.Equal("https://api.groq.com/openai/v1", Get("Integrations.OpenAI.Endpoint"));
            Assert.Null(Get("Integrations.AzureOpenAI.ApiKey"));

            // Second run: nothing left to migrate, values unchanged.
            Assert.Equal(0, IntegrationSettingsStore.MigrateLegacy(_connection));
            Assert.Equal("gsk-legacy", Get("Integrations.OpenAI.ApiKey"));
        }

        [Fact]
        public void MigrateLegacy_NeverOverwritesExistingPerProviderValues()
        {
            Set("Integrations.Provider", "OpenAI");
            Set("Integrations.OpenAI.ApiKey", "gsk-new");
            Set("Integrations.ApiKey", "gsk-old");

            IntegrationSettingsStore.MigrateLegacy(_connection);

            Assert.Equal("gsk-new", Get("Integrations.OpenAI.ApiKey"));
            Assert.Equal(0, CountLegacy());
        }

        [Theory]
        [InlineData("AzureOpenAI", "https://x.example.com/", "AzureOpenAI")]
        [InlineData("Local", "https://contoso.openai.azure.com/", "AzureOpenAI")]
        [InlineData("Local", "https://api.groq.com/openai/v1", "OpenAI")]
        [InlineData(null, "", "OpenAI")]
        public void MigrateLegacy_TargetProviderRule(string? active, string endpoint, string expectedTarget)
        {
            if (active != null) Set("Integrations.Provider", active);
            Set("Integrations.ApiKey", "k");
            Set("Integrations.Endpoint", endpoint);

            IntegrationSettingsStore.MigrateLegacy(_connection);

            Assert.Equal("k", Get($"Integrations.{expectedTarget}.ApiKey"));
            Assert.Equal(endpoint, Get($"Integrations.{expectedTarget}.Endpoint"));
            Assert.Equal(0, CountLegacy());
        }
    }
}
