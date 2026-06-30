using System;
using System.Threading.Tasks;

namespace SmartOps.Web.Tests
{
    internal class FakeAIOps : SmartOps.Web.Services.IAIOpsService
    {
        public Task<string> ExecutePromptAsync(string userPrompt)
        {
            return Task.FromResult("## 🔍 Resumen del Error\n\n- Detalle simulado\n\n## 🛠️ Acciones Recomendadas para el Operador\n\n- Verificar tarjeta\n\n## 📨 Plantilla de Correo para el Cliente\n\n- Hola, lo sentimos...");
        }
    }
}
