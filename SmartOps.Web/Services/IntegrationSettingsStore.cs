using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SmartOps.Web.Services
{
    /// <summary>Credentials of one real AI provider (OpenAI-compatible or Azure OpenAI).</summary>
    public sealed record ProviderCredentials(string ApiKey, string ModelId, string Endpoint)
    {
        public static ProviderCredentials Empty { get; } = new(string.Empty, string.Empty, string.Empty);
    }

    /// <summary>Active provider plus its own credentials, as read by the AI engine.</summary>
    public sealed record ActiveIntegration(string? Provider, ProviderCredentials Credentials);

    /// <summary>
    /// Integrations configuration persisted in the key-value <c>Settings</c> table, one set of credentials per provider:
    /// <list type="bullet">
    /// <item><description><c>Integrations.Provider</c>: active provider (<c>Local</c>, <c>OpenAI</c>, <c>AzureOpenAI</c>).</description></item>
    /// <item><description><c>Integrations.{OpenAI|AzureOpenAI}.{ApiKey|ModelId|Endpoint}</c>: that provider's values.</description></item>
    /// </list>
    /// Switching provider never reuses the other provider's values. The legacy global keys
    /// (<c>Integrations.ApiKey|ModelId|Endpoint</c>) are moved by <see cref="MigrateLegacy"/> at startup.
    /// </summary>
    public sealed class IntegrationSettingsStore
    {
        public const string ProviderKey = "Integrations.Provider";
        public const string OpenAI = "OpenAI";
        public const string AzureOpenAI = "AzureOpenAI";
        public const string Local = "Local";

        private static readonly string[] Fields = { "ApiKey", "ModelId", "Endpoint" };

        private readonly AppDbContextAccessor _db;

        public IntegrationSettingsStore(SmartOps.Infrastructure.Data.AppDbContext db)
        {
            _db = new AppDbContextAccessor(db ?? throw new ArgumentNullException(nameof(db)));
        }

        public static string Key(string provider, string field) => $"Integrations.{provider}.{field}";

        /// <summary>Normalizes any persisted provider value to one of the three known names.</summary>
        public static string Normalize(string? provider)
        {
            if (string.IsNullOrWhiteSpace(provider)) return Local;
            if (provider.Contains("azure", StringComparison.OrdinalIgnoreCase)) return AzureOpenAI;
            if (provider.Equals(OpenAI, StringComparison.OrdinalIgnoreCase)) return OpenAI;
            return Local;
        }

        public string ReadActiveProvider() => Normalize(_db.Read(ProviderKey));

        public ProviderCredentials Read(string provider)
        {
            provider = Normalize(provider);
            if (provider == Local) return ProviderCredentials.Empty;
            return new ProviderCredentials(
                _db.Read(Key(provider, "ApiKey")) ?? string.Empty,
                _db.Read(Key(provider, "ModelId")) ?? string.Empty,
                _db.Read(Key(provider, "Endpoint")) ?? string.Empty);
        }

        public ActiveIntegration ReadActive()
        {
            var raw = _db.Read(ProviderKey);
            var provider = Normalize(raw);
            return new ActiveIntegration(raw is null ? null : provider, Read(provider));
        }

        /// <summary>Marks <paramref name="provider"/> active and, for a real provider, stores its credentials.</summary>
        public void Save(string provider, ProviderCredentials? credentials)
        {
            provider = Normalize(provider);
            var values = new List<KeyValuePair<string, string>> { new(ProviderKey, provider) };
            if (provider != Local && credentials is not null)
            {
                values.Add(new(Key(provider, "ApiKey"), credentials.ApiKey?.Trim() ?? string.Empty));
                values.Add(new(Key(provider, "ModelId"), credentials.ModelId?.Trim() ?? string.Empty));
                values.Add(new(Key(provider, "Endpoint"), credentials.Endpoint?.Trim() ?? string.Empty));
            }
            _db.Write(values);
        }

        /// <summary>
        /// Idempotent startup migration of the legacy global keys to the per-provider ones, in one transaction:
        /// copies with INSERT OR IGNORE (never overwrites a per-provider value) and then deletes the legacy keys.
        /// Target: the active provider when it is real; otherwise Azure if the legacy endpoint is *.openai.azure.com,
        /// else OpenAI. Logs which keys were moved, never their values. Returns the number of keys copied.
        /// </summary>
        public static int MigrateLegacy(DbConnection connection, ILogger? logger = null)
        {
            if (connection.State != ConnectionState.Open) connection.Open();

            string? ReadRaw(string key, DbTransaction tx)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT Value FROM Settings WHERE Key = @k LIMIT 1";
                AddParam(cmd, "@k", key);
                return cmd.ExecuteScalar() as string;
            }

            using var tx = connection.BeginTransaction();
            var legacy = new Dictionary<string, string?>();
            foreach (var field in Fields)
            {
                legacy[field] = ReadRaw($"Integrations.{field}", tx);
            }

            var present = new List<string>();
            foreach (var kv in legacy) if (kv.Value is not null) present.Add(kv.Key);
            if (present.Count == 0)
            {
                tx.Commit();
                return 0;
            }

            var active = Normalize(ReadRaw(ProviderKey, tx));
            var target = active != Local
                ? active
                : (legacy["Endpoint"]?.Contains(".openai.azure.com", StringComparison.OrdinalIgnoreCase) == true ? AzureOpenAI : OpenAI);

            var copied = new List<string>();
            foreach (var field in present)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = "INSERT OR IGNORE INTO Settings (Key, Value) VALUES (@k, @v);";
                AddParam(insert, "@k", Key(target, field));
                AddParam(insert, "@v", legacy[field] ?? string.Empty);
                if (insert.ExecuteNonQuery() > 0) copied.Add(field);
            }

            using (var delete = connection.CreateCommand())
            {
                delete.Transaction = tx;
                delete.CommandText = "DELETE FROM Settings WHERE Key IN ('Integrations.ApiKey', 'Integrations.ModelId', 'Integrations.Endpoint');";
                delete.ExecuteNonQuery();
            }

            tx.Commit();

            try
            {
                logger?.LogInformation(
                    "Integrations settings migrated to provider {Provider}: copied [{Copied}], legacy keys removed [{Removed}].",
                    target, string.Join(", ", copied), string.Join(", ", present));
            }
            catch
            {
                // logging must never break startup
            }

            return copied.Count;
        }

        private static void AddParam(DbCommand cmd, string name, object value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            cmd.Parameters.Add(p);
        }

        /// <summary>Raw SQL access to the Settings table over the DbContext's connection (not disposed here).</summary>
        private sealed class AppDbContextAccessor
        {
            private readonly SmartOps.Infrastructure.Data.AppDbContext _db;

            public AppDbContextAccessor(SmartOps.Infrastructure.Data.AppDbContext db) => _db = db;

            private DbConnection Open()
            {
                var conn = _db.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open) conn.Open();
                return conn;
            }

            public string? Read(string key)
            {
                var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Value FROM Settings WHERE Key = @k LIMIT 1";
                AddParam(cmd, "@k", key);
                return cmd.ExecuteScalar() as string;
            }

            public void Write(IEnumerable<KeyValuePair<string, string>> values)
            {
                var conn = Open();
                using var tx = conn.BeginTransaction();
                foreach (var kv in values)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = "INSERT OR REPLACE INTO Settings (Key, Value) VALUES (@k, @v);";
                    AddParam(cmd, "@k", kv.Key);
                    AddParam(cmd, "@v", kv.Value);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
        }
    }
}
