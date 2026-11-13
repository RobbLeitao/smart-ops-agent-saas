using SmartOps.Web.Services;
using Xunit;

namespace SmartOps.Web.Tests
{
    public sealed class SuggestedMessageExtractorTests
    {
        [Fact]
        public void SimulatorFormat_WithEmojis_ExtractsOnlyCustomerMessage()
        {
            var markdown = "## 🔍 Razón del fallo\nEmisor rechazó la tarjeta\n\n## 🛠️ Acción recomendada para el operador\n- Revisar con el emisor.\n\n## 📨 Mensaje sugerido para el cliente\nSu emisor ha rechazado la transacción. Por favor intente con otra tarjeta o contacte a su banco.";

            Assert.True(SuggestedMessageExtractor.TryExtractPlainText(markdown, out var message));
            Assert.Equal("Su emisor ha rechazado la transacción. Por favor intente con otra tarjeta o contacte a su banco.", message);
        }

        [Fact]
        public void RealLlmFormat_WithoutEmojis_WithQuotesAndBold_ReturnsPlainText()
        {
            // Actual shape returned by Groq (openai/gpt-oss-20b): no emojis, different heading wording,
            // blockquote, bold, Markdown hard line breaks (two trailing spaces), CRLF.
            var markdown =
                "## Razón del fallo  \r\n" +
                "- **Card Declined**: El emisor rechazó la tarjeta.\r\n\r\n" +
                "## Acción recomendada para el operador  \r\n" +
                "1. **Verificar estado de la tarjeta**  \r\n\r\n" +
                "## Mensaje sugerido para enviar al cliente  \r\n" +
                "> Estimado/a **[Nombre del cliente]**  \r\n" +
                ">   \r\n" +
                "> Lamentamos informarle que su intento de pago de **$98.54 USD** ha sido rechazado por su banco.  \r\n" +
                "> Para completar la transacción, le recomendamos:\r\n" +
                "> 1. Verificar que su tarjeta tenga fondos suficientes.  \r\n" +
                "> 2. Si dispone de otra tarjeta, puede intentar nuevamente el pago.  \r\n" +
                ">  \r\n" +
                "> Atentamente,  \r\n" +
                "> Equipo de Soporte Técnico\r\n";

            Assert.True(SuggestedMessageExtractor.TryExtractPlainText(markdown, out var message));
            Assert.Equal(
                "Estimado/a [Nombre del cliente]\n\n" +
                "Lamentamos informarle que su intento de pago de $98.54 USD ha sido rechazado por su banco.\n" +
                "Para completar la transacción, le recomendamos:\n" +
                "1. Verificar que su tarjeta tenga fondos suficientes.\n" +
                "2. Si dispone de otra tarjeta, puede intentar nuevamente el pago.\n\n" +
                "Atentamente,\n" +
                "Equipo de Soporte Técnico",
                message);
            Assert.DoesNotContain(">", message);
            Assert.DoesNotContain("**", message);
            Assert.DoesNotContain("##", message);
            Assert.DoesNotContain("\r", message);
        }

        [Theory]
        [InlineData("### 3. Mensaje al cliente")]
        [InlineData("# MENSAJE SUGERIDO")]
        [InlineData("**Mensaje sugerido para el cliente:**")]
        [InlineData("3. **Mensaje sugerido para el cliente**")]
        [InlineData("## 📨 Mensaje Sugerido Para El Cliente ##")]
        public void HeadingVariants_AreRecognized(string heading)
        {
            var markdown = $"## Razón del fallo\nFondos insuficientes\n\n{heading}\nHola, su pago no pudo procesarse.";

            Assert.True(SuggestedMessageExtractor.TryExtractPlainText(markdown, out var message));
            Assert.Equal("Hola, su pago no pudo procesarse.", message);
        }

        [Theory]
        [InlineData("## Notas internas")]
        [InlineData("---")]
        [InlineData("**Acción recomendada para el operador**")]
        public void MessageEndsAtNextSection(string nextSection)
        {
            var markdown = $"## Mensaje sugerido para el cliente\nHola, su pago no pudo procesarse.\n\n{nextSection}\nTexto que no es del cliente.";

            Assert.True(SuggestedMessageExtractor.TryExtractPlainText(markdown, out var message));
            Assert.Equal("Hola, su pago no pudo procesarse.", message);
        }

        [Theory]
        [InlineData("## 🔍 Razón del fallo\nTarjeta vencida\n\n## 🛠️ Acción recomendada para el operador\n- Pedir actualización.")]
        [InlineData("op")]
        [InlineData("Diagnóstico viejo sin secciones. Mensaje para el cliente: hola")]
        [InlineData("## Mensaje sugerido para el cliente\n\n   \n")]
        [InlineData("## Mensaje sugerido para el cliente\n-")]
        public void WithoutRecognizableSection_ReturnsFalse(string markdown)
        {
            Assert.False(SuggestedMessageExtractor.TryExtractPlainText(markdown, out var message));
            Assert.Equal(string.Empty, message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   \n  ")]
        public void EmptyText_ReturnsFalse(string? markdown)
        {
            Assert.False(SuggestedMessageExtractor.TryExtractPlainText(markdown, out var message));
            Assert.Equal(string.Empty, message);
        }
    }
}
