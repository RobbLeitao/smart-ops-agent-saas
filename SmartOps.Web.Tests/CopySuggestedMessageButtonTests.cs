using System;
using System.Linq;
using Bunit;
using Microsoft.JSInterop;
using SmartOps.Web.Components.Layout;
using Xunit;

namespace SmartOps.Web.Tests
{
    public sealed class CopySuggestedMessageButtonTests : IDisposable
    {
        private const string Markdown =
            "## Razón del fallo\nFondos insuficientes\n\n## Mensaje sugerido para enviar al cliente\n> Estimado cliente, **su pago** no pudo procesarse.";

        private const string ExpectedPlainText = "Estimado cliente, su pago no pudo procesarse.";

        private readonly TestContext _ctx = new();

        public void Dispose() => _ctx.Dispose();

        private IRenderedComponent<CopySuggestedMessageButton> Render(string? markdown, TimeSpan? confirmation = null)
            => _ctx.Render<CopySuggestedMessageButton>(p => p
                .Add(x => x.Markdown, markdown)
                .Add(x => x.ConfirmationDuration, confirmation ?? TimeSpan.FromMilliseconds(100)));

        [Fact]
        public void Copy_Success_ShowsCopiedState_ThenReturnsToNormal()
        {
            var copy = _ctx.JSInterop.Setup<bool>("smartOpsCopyText", ExpectedPlainText);
            copy.SetResult(true);
            var cut = Render(Markdown);

            Assert.Contains("Copiar Mensaje", cut.Find("button.copy-btn").TextContent);

            cut.Find("button.copy-btn").Click();

            var button = cut.Find("button.copy-btn");
            Assert.Contains("¡Copiado!", button.TextContent);
            Assert.Contains("copied", button.ClassName);
            Assert.NotNull(cut.Find(".bi-check2"));
            // Only the plain-text customer message is sent to the clipboard.
            Assert.Equal(ExpectedPlainText, Assert.Single(copy.Invocations).Arguments.Single());

            cut.WaitForAssertion(
                () => Assert.Contains("Copiar Mensaje", cut.Find("button.copy-btn").TextContent),
                TimeSpan.FromSeconds(2));
            Assert.DoesNotContain("copied", cut.Find("button.copy-btn").ClassName);
        }

        [Fact]
        public void Copy_ReturnsFalse_ShowsClearError()
        {
            _ctx.JSInterop.Setup<bool>("smartOpsCopyText", ExpectedPlainText).SetResult(false);
            var cut = Render(Markdown);

            cut.Find("button.copy-btn").Click();

            Assert.Contains("No se pudo copiar al portapapeles", cut.Find(".copy-error").TextContent);
            Assert.DoesNotContain("¡Copiado!", cut.Markup);
        }

        [Fact]
        public void Copy_JsThrows_ShowsClearError_WithoutUnhandledException()
        {
            _ctx.JSInterop.Setup<bool>("smartOpsCopyText", ExpectedPlainText)
                .SetException(new JSException("NotAllowedError: Document is not focused."));
            var cut = Render(Markdown);

            cut.Find("button.copy-btn").Click();

            Assert.Contains("No se pudo copiar al portapapeles", cut.Find(".copy-error").TextContent);
        }

        [Theory]
        [InlineData("## 🔍 Razón del fallo\nTarjeta vencida")]
        [InlineData("")]
        [InlineData(null)]
        public void WithoutCustomerMessage_ButtonDisabled_ShowsNotice_AndNeverCallsJs(string? markdown)
        {
            var cut = Render(markdown);

            Assert.True(cut.Find("button.copy-btn").HasAttribute("disabled"));
            Assert.Contains(CopySuggestedMessageButton.NoMessageText, cut.Find(".copy-note").TextContent);
            Assert.Empty(_ctx.JSInterop.Invocations);
        }
    }
}
