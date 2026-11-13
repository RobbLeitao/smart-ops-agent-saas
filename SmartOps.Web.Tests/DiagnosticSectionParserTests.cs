using SmartOps.Web.Services;
using Xunit;

namespace SmartOps.Web.Tests;

public sealed class DiagnosticSectionParserTests
{
    [Fact]
    public void SimulatorFormat_WithEmojis()
    {
        var md = "## 🔍 Resumen del Error\nEl emisor rechazó la tarjeta.\n\n" +
                 "## 🛠️ Acciones Recomendadas para el Operador\n- Verificar con el banco.\n- Ofrecer otra tarjeta.\n\n" +
                 "## 📨 Mensaje sugerido para el cliente\nHola, tu pago fue rechazado por tu banco.";

        var s = DiagnosticSectionParser.Parse(md);

        Assert.True(s.HasSections);
        Assert.Equal("El emisor rechazó la tarjeta.", s.WhatHappened);
        Assert.Equal("1. Verificar con el banco.\n2. Ofrecer otra tarjeta.", s.WhatToDo);
        Assert.Equal("Hola, tu pago fue rechazado por tu banco.", s.CustomerMessage);
        Assert.Null(s.Other);
    }

    [Fact]
    public void LlmFormat_WithoutEmojis_NumberedHeadingsAndSeparators()
    {
        var md = "### 1. Razón del fallo  \r\n" +
                 "- **Card Declined**: el emisor rechazó la tarjeta.\r\n\r\n" +
                 "---\r\n" +
                 "### 2. Acción recomendada para el operador\r\n" +
                 "1. **Verificar estado de la tarjeta**\r\n" +
                 "2. Registrar el incidente.\r\n\r\n" +
                 "**Mensaje sugerido para enviar al cliente:**\r\n" +
                 "> Hola, tu pago de **$98.54** fue rechazado.\r\n";

        var s = DiagnosticSectionParser.Parse(md);

        Assert.True(s.HasSections);
        Assert.Contains("Card Declined", s.WhatHappened);
        Assert.DoesNotContain("---", s.WhatHappened);
        Assert.StartsWith("1. **Verificar estado de la tarjeta**", s.WhatToDo);
        Assert.Contains("2. Registrar el incidente.", s.WhatToDo);
        Assert.Contains("fue rechazado", s.CustomerMessage);

        // Se renderiza como HTML, nunca crudo.
        var html = DiagnosticMarkdown.ToHtml(s.WhatToDo);
        Assert.Contains("<ol>", html);
        Assert.Contains("<strong>Verificar estado de la tarjeta</strong>", html);
    }

    [Fact]
    public void LlmFormat_WrappedInMarkdownFence_IsUnwrapped()
    {
        // Respuesta real de gpt-oss-20b (Groq): todo el diagnóstico dentro de ```markdown … ```.
        var md = "```markdown\n# Diagnóstico de la Transacción Fallida\n\n" +
                 "## Razón del fallo  \n- **Error**: *Suspected fraud*.\n\n" +
                 "## Acción recomendada para el operador  \n1. **Revisar la cuenta**  \n   - Verificar la IP.\n\n" +
                 "## Mensaje sugerido para el cliente  \nHola, estamos revisando tu pago.\n```";

        var s = DiagnosticSectionParser.Parse(md);

        Assert.True(s.HasSections);
        Assert.DoesNotContain("```", s.WhatHappened);
        Assert.DoesNotContain("<pre>", DiagnosticMarkdown.ToHtml(s.WhatHappened));
        Assert.Contains("<strong>Error</strong>", DiagnosticMarkdown.ToHtml(s.WhatHappened));
        Assert.Equal("Hola, estamos revisando tu pago.", s.CustomerMessage);

        Assert.True(SuggestedMessageExtractor.TryExtractPlainText(md, out var copy));
        Assert.Equal("Hola, estamos revisando tu pago.", copy);
    }

    [Fact]
    public void UnknownHeading_IsKeptApart_AndDoesNotLeakIntoCustomerMessage()
    {
        var md = "## Mensaje sugerido para el cliente\nHola.\n\n## Notas internas\nNo enviar al cliente.";

        var s = DiagnosticSectionParser.Parse(md);

        Assert.Equal("Hola.", s.CustomerMessage);
        Assert.Contains("Notas internas", s.Other);
        Assert.Contains("No enviar al cliente.", s.Other);
    }

    [Fact]
    public void NoSections_FallsBackToFullMarkdownRendered()
    {
        var md = "El pago falló por **fondos insuficientes**.\n\n- Reintentar más tarde.";

        var s = DiagnosticSectionParser.Parse(md);

        Assert.False(s.HasSections);
        Assert.Equal(md, s.FullMarkdown);
        var html = DiagnosticMarkdown.ToHtml(s.FullMarkdown);
        Assert.Contains("<strong>fondos insuficientes</strong>", html);
        Assert.Contains("<li>Reintentar más tarde.</li>", html);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void Empty_HasNoSections(string? md)
    {
        var s = DiagnosticSectionParser.Parse(md);
        Assert.False(s.HasSections);
        Assert.Equal(string.Empty, DiagnosticMarkdown.ToHtml(md));
    }

    [Fact]
    public void RawHtmlFromLlm_IsNeverInjected()
    {
        var html = DiagnosticMarkdown.ToHtml("Hola <script>alert(1)</script> <b>x</b>");
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<b>", html);
        Assert.Contains("&lt;script&gt;", html);
    }
}
