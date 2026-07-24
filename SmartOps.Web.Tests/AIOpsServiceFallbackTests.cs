using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Xunit;

namespace SmartOps.Web.Tests
{
    public sealed class AIOpsServiceFallbackTests
    {
        [Fact]
        public async Task ExecutePromptAsync_FallbackGeneratesSimulatedResponse_WhenNoKernelService()
        {
            // Build an empty Kernel (no connectors) to trigger the fallback path inside AIOpsService
            var kb = Kernel.CreateBuilder();
            var kernel = kb.Build();

            var svc = new SmartOps.Web.Services.AIOpsService(kernel);

            var prompt = "Some context\nErrorMessage: expired card"
;            var result = await svc.ExecutePromptAsync(prompt);

            Assert.Contains("Tarjeta vencida", result);
            Assert.Contains("Estimado cliente", result);
        }
    }
}
