using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SmartOps.Core.Entities;
using SmartOps.Infrastructure.Data;

namespace SmartOps.Web.Services
{
    /// <summary>
    /// Runtime <see cref="ITransactionAnalyzer"/> switch: reads the Provider persisted from /integrations
    /// on every call and delegates to the Local simulator or the real AI engine accordingly. This lets an
    /// operator change providers from the UI without restarting the app.
    /// </summary>
    public sealed class ProviderSwitchingTransactionAnalyzer : ITransactionAnalyzer
    {
        private readonly ITransactionAnalyzer _localAnalyzer;
        private readonly ITransactionAnalyzer _realAnalyzer;
        private readonly AppDbContext? _db;

        public ProviderSwitchingTransactionAnalyzer(
            DevFakeTransactionAnalyzer localAnalyzer,
            AzureOpenAiDiagnosticEngine realAnalyzer,
            AppDbContext? db = null)
        {
            _localAnalyzer = localAnalyzer ?? throw new ArgumentNullException(nameof(localAnalyzer));
            _realAnalyzer = realAnalyzer ?? throw new ArgumentNullException(nameof(realAnalyzer));
            _db = db;
        }

        public Task<string> AnalyzeAsync(Transaction tx)
        {
            var provider = ReadPersistedProvider();
            var useReal = !string.IsNullOrWhiteSpace(provider)
                          && !provider.Equals("Local", StringComparison.OrdinalIgnoreCase);

            return useReal ? _realAnalyzer.AnalyzeAsync(tx) : _localAnalyzer.AnalyzeAsync(tx);
        }

        private string? ReadPersistedProvider()
        {
            if (_db == null) return null;

            try
            {
                var conn = _db.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open) conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Value FROM Settings WHERE Key = @k LIMIT 1";
                var param = cmd.CreateParameter();
                param.ParameterName = "@k";
                param.Value = "Integrations.Provider";
                cmd.Parameters.Add(param);

                return cmd.ExecuteScalar() as string;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
