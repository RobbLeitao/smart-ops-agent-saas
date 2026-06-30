using System;
using System.Threading.Tasks;
using Xunit;

namespace SmartOps.Web.Tests;

public sealed class DiagnosticOrchestratorServiceTests
{
    private sealed class FakeAIOpsService : SmartOps.Web.Services.IAIOpsService
    {
        public Task<string> ExecutePromptAsync(string userPrompt)
        {
            // Simulate model returning a structured markdown response
            var md = "## 🔍 Resumen del Error\nDetalle del error\n\n## 🛠️ Acciones Recomendadas para el Operador\n- Revisar logs\n\n## 📨 Plantilla de Correo para el Cliente\nEstimado cliente...";
            return Task.FromResult(md);
        }
    }

    [Fact]
    public async Task RunDiagnosticAsync_ReturnsStructuredMarkdown()
    {
        var fake = new FakeAIOpsService();
        var svc = new SmartOps.Web.Services.DiagnosticOrchestratorService(fake);

        var result = await svc.RunDiagnosticAsync(Guid.NewGuid());

        Assert.Contains("## 🔍 Resumen del Error", result);
        Assert.Contains("## 🛠️ Acciones Recomendadas para el Operador", result);
        Assert.Contains("## 📨 Plantilla de Correo para el Cliente", result);
    }
}
