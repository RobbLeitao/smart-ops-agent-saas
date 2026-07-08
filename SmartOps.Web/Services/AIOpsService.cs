using System.Threading.Tasks;
using Microsoft.SemanticKernel;

namespace SmartOps.Web.Services
{
    public interface IAIOpsService
    {
        Task<string> ExecutePromptAsync(string userPrompt);
    }

    public sealed class AIOpsService : IAIOpsService
    {
        private readonly Kernel _kernel;

        public AIOpsService(Kernel kernel)
        {
            _kernel = kernel ?? throw new System.ArgumentNullException(nameof(kernel));
        }

        public async Task<string> ExecutePromptAsync(string userPrompt)
        {
            if (userPrompt is null) throw new System.ArgumentNullException(nameof(userPrompt));

            var settings = new PromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };

            try
            {
                var result = await _kernel.InvokePromptAsync(userPrompt, new(settings));
                return result.ToString();
            }
            catch (System.Exception ex)
            {
                if (ex.Message != null && ex.Message.Contains("No service was found", System.StringComparison.OrdinalIgnoreCase))
                {
                    // Better fallback: attempt to extract ErrorMessage or transaction details from the prompt
                    try
                    {
                        string errorLine = null;
                        using (var sr = new System.IO.StringReader(userPrompt))
                        {
                            string? line;
                            while ((line = sr.ReadLine()) != null)
                            {
                                if (line.TrimStart().StartsWith("ErrorMessage:", System.StringComparison.OrdinalIgnoreCase))
                                {
                                    errorLine = line.Substring(line.IndexOf(':') + 1).Trim();
                                    break;
                                }
                            }
                        }

                        string generated;
                        if (!string.IsNullOrWhiteSpace(errorLine))
                        {
                            // Lightweight heuristics to mimic the DevFakeGenerator
                            var lower = errorLine.ToLowerInvariant();
                            if (lower.Contains("expired") || lower.Contains("venc"))
                            {
                                generated = "## 🔍 Razón del fallo\nTarjeta vencida\n\n## 🛠️ Acción recomendada para el operador\n- Solicitar al cliente que actualice la fecha de vencimiento.\n- Reintentar el cobro tras actualizar los datos.\n\n## 📨 Mensaje sugerido para el cliente\nEstimado cliente, su tarjeta ha expirado. Por favor actualice la fecha de vencimiento y reintente el pago.";
                            }
                            else if (lower.Contains("insufficient" ) || lower.Contains("fondos"))
                            {
                                generated = "## 🔍 Razón del fallo\nFondos insuficientes\n\n## 🛠️ Acción recomendada para el operador\n- Sugerir reintentar en 24 horas.\n- Ofrecer cambiar de método de pago.\n\n## 📨 Mensaje sugerido para el cliente\nEstimado cliente, su tarjeta no tiene fondos suficientes en este momento. Puede intentar nuevamente más tarde o utilizar otro método de pago.";
                            }
                            else if (lower.Contains("suspected") || lower.Contains("fraud"))
                            {
                                generated = "## 🔍 Razón del fallo\nPosible fraude detectado\n\n## 🛠️ Acción recomendada para el operador\n- Bloquear la transacción provisionalmente.\n- Revisar IP, país y patrones de comportamiento.\n- Contactar al cliente por verificación.\n\n## 📨 Mensaje sugerido para el cliente\nHemos detectado actividad sospechosa en su pago. Nos comunicaremos para verificar la transacción.";
                            }
                            else
                            {
                                generated = "## 🔍 Resumen del Error\nNo hay proveedor de IA configurado. Resultado simulado.\n\n## 🛠️ Acciones Recomendadas para el Operador\n- Revisar la configuración de AI en appsettings.json o las variables de entorno.\n\n## 📨 Mensaje sugerido para el cliente\nEstimado cliente, estamos investigando su pago.";
                            }

                            return generated;
                        }
                    }
                    catch
                    {
                        // fallthrough to canned message
                    }

                    // Canned fallback
                    return "## 🔍 Resumen del Error\nNo hay proveedor de IA configurado. Resultado simulado.\n\n## 🛠️ Acciones Recomendadas para el Operador\n- Revisar la configuración de AI en appsettings.json o las variables de entorno.\n\n## 📨 Mensaje sugerido para el cliente\nEstimado cliente, estamos investigando su pago.";
                }

                // Bubble up other exceptions as string to callers so UI can display them.
                return $"Error executing AI prompt: {ex.Message}";
            }
        }
    }
}
