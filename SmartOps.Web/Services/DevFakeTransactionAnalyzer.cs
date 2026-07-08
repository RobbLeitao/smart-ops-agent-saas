using System.Threading.Tasks;
using SmartOps.Core.Entities;
using System.Threading;

namespace SmartOps.Web.Services
{
    // Lightweight transaction analyzer used in dev when no OpenAI key is configured.
    public sealed class DevFakeTransactionAnalyzer : ITransactionAnalyzer
    {
        public async Task<string> AnalyzeAsync(Transaction tx)
        {
            // Keep an artificial delay so UI spinner is visible
            await Task.Delay(2000);

            var input = tx?.ErrorMessage ?? string.Empty;
            var lower = input.ToLowerInvariant();
            string reason = "Diagnóstico genérico";
            string actions = "- Revisar logs y reintentar.";
            string customer = "Estimado cliente, estamos investigando su pago.";

            if (lower.Contains("expired") || lower.Contains("venc"))
            {
                reason = "Tarjeta vencida";
                actions = "- Solicitar al cliente que actualice la fecha de vencimiento.\n- Reintentar el cobro tras actualizar los datos.";
                customer = "Estimado cliente, su tarjeta ha expirado. Por favor actualice la fecha de vencimiento y reintente el pago.";
            }
            else if (lower.Contains("insufficient") || lower.Contains("fondos"))
            {
                reason = "Fondos insuficientes";
                actions = "- Sugerir reintentar en 24 horas.\n- Ofrecer cambiar de método de pago.";
                customer = "Estimado cliente, su tarjeta no tiene fondos suficientes en este momento. Puede intentar nuevamente más tarde o utilizar otro método de pago.";
            }
            else if (lower.Contains("suspected") || lower.Contains("fraud") || lower.Contains("sospecha"))
            {
                reason = "Posible fraude detectado";
                actions = "- Bloquear la transacción provisionalmente.\n- Revisar IP, país y patrones de comportamiento.\n- Contactar al cliente por verificación.";
                customer = "Hemos detectado actividad sospechosa en su pago. Nos comunicaremos para verificar la transacción.";
            }
            else if (lower.Contains("declined") || lower.Contains("card declined"))
            {
                reason = "Emisor rechazó la tarjeta";
                actions = "- Revisar con el emisor por código de rechazo.\n- Sugerir al cliente intentar con otra tarjeta.";
                customer = "Su emisor ha rechazado la transacción. Por favor intente con otra tarjeta o contacte a su banco.";
            }
            else if (lower.Contains("cvc") || lower.Contains("invalid cvc") || lower.Contains("cvc incorrect"))
            {
                reason = "Código CVC inválido";
                actions = "- Verificar que el cliente ingresó correctamente el CVC.\n- Permitir reintento inmediato.";
                customer = "Parece que el código de seguridad (CVC) es incorrecto. Por favor verifique sus datos e intente nuevamente.";
            }

            var md = $"## 🔍 Razón del fallo\n{reason}\n\n## 🛠️ Acción recomendada para el operador\n{actions}\n\n## 📨 Mensaje sugerido para el cliente\n{customer}";
            return md;
        }
    }
}
