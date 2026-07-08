using System.Threading.Tasks;
using SmartOps.Core.Entities;

namespace SmartOps.Web.Services
{
    // Thin adapter that delegates to the existing orchestrator which uses IAIOpsService (Kernel/OpenAI).
    public sealed class OpenAITransactionAnalyzer : ITransactionAnalyzer
    {
        private readonly DiagnosticOrchestratorService _orchestrator;

        public OpenAITransactionAnalyzer(DiagnosticOrchestratorService orchestrator)
        {
            _orchestrator = orchestrator;
        }

        public Task<string> AnalyzeAsync(Transaction tx)
        {
            // Delegate to orchestrator which builds a full prompt and calls IAIOpsService
            return _orchestrator.RunDiagnosticAsync(tx);
        }
    }
}
