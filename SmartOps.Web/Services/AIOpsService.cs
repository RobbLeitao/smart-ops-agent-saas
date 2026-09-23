using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.EntityFrameworkCore;

namespace SmartOps.Web.Services
{
    public interface IAIOpsService
    {
        Task<string> ExecutePromptAsync(string userPrompt);
    }

    public sealed class AIOpsService : IAIOpsService
    {
        // Artificial "thinking" delay applied only to the local/no-provider-configured fallback
        // path below (the "Local Simulator" experience), so the AI diagnostic feels like real
        // background work and callers' loading UI (e.g. TransactionDetailDrawer's loader) has
        // time to be perceived, instead of resolving synchronously/instantly. Lives in this
        // engine layer, not in any UI component, so every screen that calls ExecutePromptAsync
        // gets the same consistent behavior. Real provider calls above (user OpenAI key / the
        // injected Kernel when actually configured) are never delayed here.
        private static readonly TimeSpan SimulatedThinkingDelay = TimeSpan.FromMilliseconds(1800);

        private readonly Kernel _kernel;
        private readonly SmartOps.Infrastructure.Data.AppDbContext? _db;

        public AIOpsService(Kernel kernel, SmartOps.Infrastructure.Data.AppDbContext? db = null)
        {
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _db = db;
        }

        public async Task<string> ExecutePromptAsync(string userPrompt)
        {
            if (userPrompt is null) throw new ArgumentNullException(nameof(userPrompt));

            var settings = new PromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };

            // First, try to read persisted integration settings to see if we should use OpenAI with a user key.
            try
            {
                if (_db != null)
                {
                    var conn = _db.Database.GetDbConnection();
                    if (conn.State != System.Data.ConnectionState.Open) conn.Open();

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT Value FROM Settings WHERE Key = @k LIMIT 1";
                    var p = cmd.CreateParameter(); p.ParameterName = "@k"; p.Value = "Integrations.Provider"; cmd.Parameters.Add(p);
                    var prov = cmd.ExecuteScalar() as string;

                    cmd.Parameters.Clear();
                    p = cmd.CreateParameter(); p.ParameterName = "@k"; p.Value = "Integrations.ApiKey"; cmd.Parameters.Add(p);
                    var key = cmd.ExecuteScalar() as string;

                    cmd.Parameters.Clear();
                    p = cmd.CreateParameter(); p.ParameterName = "@k"; p.Value = "Integrations.ModelId"; cmd.Parameters.Add(p);
                    var model = cmd.ExecuteScalar() as string ?? "gpt-4o";

                    if (!string.IsNullOrWhiteSpace(prov) && prov.Equals("OpenAI", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(key))
                    {
                        try
                        {
                            // Build a temporary kernel with the user-provided API key to call OpenAI.
                            // Real network call: the loader stays visible for its actual latency, no
                            // artificial delay is added here.
                            var kb = Kernel.CreateBuilder();
                            kb.AddOpenAIChatCompletion(key, null, model);
                            var userKernel = kb.Build();
                            var result = await userKernel.InvokePromptAsync(userPrompt, new(settings));
                            return result.ToString();
                        }
                        catch (Exception exUser)
                        {
                            // Log and fall back to the app's configured kernel below.
                            Console.Error.WriteLine($"OpenAI user-key invocation failed, falling back to default kernel: {exUser.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed reading integrations settings for AI: {ex.Message}");
            }

            // If we reach here, use the default injected kernel (_kernel), which may be a dev fake or
            // an environment-configured real provider.
            try
            {
                var result = await _kernel.InvokePromptAsync(userPrompt, new(settings));
                return result.ToString();
            }
            catch (Exception ex)
            {
                if (ex.Message != null && ex.Message.Contains("No service was found", StringComparison.OrdinalIgnoreCase))
                {
                    // No real AI provider is configured at all (no OpenAI key in appsettings/env and no
                    // chat completion service registered on the Kernel): this is the "Local Simulator"
                    // fallback. Simulate real background work before returning the canned/heuristic
                    // diagnosis so any caller's loader has time to be perceived.
                    await Task.Delay(SimulatedThinkingDelay);

                    // Better fallback: attempt to extract ErrorMessage or transaction details from the prompt.
                    try
                    {
                        string? errorLine = null;
                        using (var sr = new StringReader(userPrompt))
                        {
                            string? line;
                            while ((line = sr.ReadLine()) != null)
                            {
                                if (line.TrimStart().StartsWith("ErrorMessage:", StringComparison.OrdinalIgnoreCase))
                                {
                                    errorLine = line.Substring(line.IndexOf(':') + 1).Trim();
                                    break;
                                }
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(errorLine))
                        {
                            var lower = errorLine.ToLowerInvariant();
                            string generated;
                            if (lower.Contains("expired") || lower.Contains("venc"))
                            {
                                generated = "## 🔍 Razón del fallo\nTarjeta vencida\n\n## 🛠️ Acción recomendada para el operador\n- Solicitar al cliente que actualice la fecha de vencimiento.\n- Reintentar el cobro tras actualizar los datos.\n\n## 📨 Mensaje sugerido para el cliente\nEstimado cliente, su tarjeta ha expirado. Por favor actualice la fecha de vencimiento y reintente el pago.";
                            }
                            else if (lower.Contains("insufficient") || lower.Contains("fondos"))
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
