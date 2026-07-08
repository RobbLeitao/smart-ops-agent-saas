using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.TextGeneration;

namespace SmartOps.Web.Services
{
    // Dev fake adapter implementing only the Semantic Kernel interfaces the app uses in this solution.
    public sealed class DevFakeKernelAdapter : IChatCompletionService, ITextGenerationService
    {
        private readonly string _response =
            "## 🔍 Resumen del Error\nTransacción simulada: detalle del error\n\n## 🛠️ Acciones Recomendadas para el Operador\n- Revisar logs\n\n## 📨 Plantilla de Correo para el Cliente\nEstimado cliente...";

        // IChatCompletionService
        public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            // Attempt to synthesize input from chat history if present, otherwise fall back to default response
            string input = string.Empty;
            try
            {
                if (chatHistory != null)
                {
                    var msgs = new List<string>();
                    // ChatHistory APIs vary across SK versions; attempt a safe ToString() fallback
                    try
                    {
                        var s = chatHistory.ToString();
                        if (!string.IsNullOrWhiteSpace(s)) input = s;
                    }
                    catch
                    {
                        input = string.Empty;
                    }
                }
            }
            catch
            {
                input = string.Empty;
            }

            var md = GenerateMarkdownForInput(input);
            IReadOnlyList<ChatMessageContent> messages = new[] { new ChatMessageContent(AuthorRole.Assistant, md) };
            return Task.FromResult(messages);
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        // ITextGenerationService
        public Task<IReadOnlyList<TextContent>> GetTextContentsAsync(string text, PromptExecutionSettings? settings = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
        {
            var md = GenerateMarkdownForInput(text);
            var content = new TextContent(md);
            IReadOnlyList<TextContent> list = new[] { content };
            return Task.FromResult(list);
        }

        public async IAsyncEnumerable<StreamingTextContent> GetStreamingTextContentsAsync(string text, PromptExecutionSettings? settings = null, Kernel? kernel = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        private string GenerateMarkdownForInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return _response;

            var lower = input.ToLowerInvariant();
            string reason = "Diagnóstico genérico";
            string actions = "- Revisar logs y reintentar.";
            string customer = "Estimado cliente, estamos investigando su pago.";

            if (lower.Contains("expired") || lower.Contains("expired card") || lower.Contains("venc"))
            {
                reason = "Tarjeta vencida";
                actions = "- Solicitar al cliente que actualice la fecha de vencimiento.\n- Reintentar el cobro tras actualizar los datos.";
                customer = "Estimado cliente, su tarjeta ha expirado. Por favor actualice la fecha de vencimiento y reintente el pago.";
            }
            else if (lower.Contains("insufficient") || lower.Contains("insufficient funds") || lower.Contains("fondos"))
            {
                reason = "Fondos insuficientes";
                actions = "- Sugerir reintentar en 24 horas.\n- Ofrecer cambiar de método de pago.";
                customer = "Estimado cliente, su tarjeta no tiene fondos suficientes en este momento. Puede intentar nuevamente más tarde o utilizar otro método de pago.";
            }
            else if (lower.Contains("suspected") || lower.Contains("fraud") || lower.Contains("fraudulent") || lower.Contains("sospecha"))
            {
                reason = "Posible fraude detectado";
                actions = "- Bloquear la transacción provisionalmente.\n- Revisar IP, país y patrones de comportamiento.\n- Contactar al cliente por verificación.";
                customer = "Hemos detectado actividad sospechosa en su pago. Nos comunicaremos para verificar la transacción.";
            }
            else if (lower.Contains("card declined") || lower.Contains("declined") )
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
