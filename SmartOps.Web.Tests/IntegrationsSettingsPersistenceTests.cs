using System;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartOps.Infrastructure.Data;
using Xunit;

namespace SmartOps.Web.Tests
{
    public sealed class IntegrationsSettingsPersistenceTests : IDisposable
    {
        private readonly DbConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;

        public IntegrationsSettingsPersistenceTests()
        {
            // Use a real in-memory SQLite connection so we can execute SQL commands that rely on a real DB
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;

            using var db = new AppDbContext(_options);
            db.Database.EnsureCreated();
            // create Settings table as the app does at startup
            db.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT);");
        }

        [Fact]
        public void SaveAndLoad_IntegrationsSettings()
        {
            // Ensure Settings table exists by executing directly on the shared connection
            using (var ensureCmd = _connection.CreateCommand())
            {
                ensureCmd.CommandText = "CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT);";
                ensureCmd.ExecuteNonQuery();
            }

            // Insert keys using the shared connection directly
            using (var tran = _connection.BeginTransaction())
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tran;
                cmd.CommandText = "INSERT OR REPLACE INTO Settings (Key,Value) VALUES (@k,@v);";
                var pK = cmd.CreateParameter(); pK.ParameterName = "@k"; cmd.Parameters.Add(pK);
                var pV = cmd.CreateParameter(); pV.ParameterName = "@v"; cmd.Parameters.Add(pV);

                pK.Value = "Integrations.Provider"; pV.Value = "OpenAI"; cmd.ExecuteNonQuery();
                pK.Value = "Integrations.ApiKey"; pV.Value = "sk-test"; cmd.ExecuteNonQuery();
                pK.Value = "Integrations.ModelId"; pV.Value = "gpt-4o"; cmd.ExecuteNonQuery();

                tran.Commit();
            }

            // Read back using the shared connection
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT Value FROM Settings WHERE Key = @k LIMIT 1";
                var p = cmd.CreateParameter(); p.ParameterName = "@k"; p.Value = "Integrations.Provider"; cmd.Parameters.Add(p);
                var prov = cmd.ExecuteScalar() as string;
                Assert.Equal("OpenAI", prov);

                cmd.Parameters.Clear(); p = cmd.CreateParameter(); p.ParameterName = "@k"; p.Value = "Integrations.ApiKey"; cmd.Parameters.Add(p);
                var key = cmd.ExecuteScalar() as string;
                Assert.Equal("sk-test", key);

                cmd.Parameters.Clear(); p = cmd.CreateParameter(); p.ParameterName = "@k"; p.Value = "Integrations.ModelId"; cmd.Parameters.Add(p);
                var model = cmd.ExecuteScalar() as string;
                Assert.Equal("gpt-4o", model);
            }
        }

        public void Dispose()
        {
            _connection?.Dispose();
        }
    }
}
