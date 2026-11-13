using Bunit;
using Microsoft.AspNetCore.Components;
using SmartOps.Web.Services;
using Xunit;

namespace SmartOps.Web.Tests
{
    /// <summary>Login (docs/redesign/06-login.html): labels, autocomplete, inline validation, error mapping.</summary>
    public sealed class LoginPageTests : BunitContext
    {
        public LoginPageTests() => JSInterop.Mode = JSRuntimeMode.Strict;

        [Fact]
        public void Renders_LabelsAutocomplete_AndNoInlineStyle()
        {
            var cut = Render<SmartOps.Web.Components.Pages.Login>();

            Assert.Equal("Iniciá sesión", cut.Find("h1").TextContent);
            Assert.Equal("username", cut.Find("#login-email").GetAttribute("autocomplete"));
            Assert.Equal("current-password", cut.Find("#login-password").GetAttribute("autocomplete"));
            Assert.Equal("login-email", cut.Find("label[for=login-email]").GetAttribute("for"));
            Assert.Equal("Ingresar", cut.Find("button[type=submit]").TextContent);
            Assert.Contains("¿No tenés acceso? Pedíselo a un Auditor de tu equipo.", cut.Markup);
            Assert.DoesNotContain("<style", cut.Markup);
        }

        [Fact]
        public void EmptyFields_ValidateInline_WithoutCallingServer()
        {
            var cut = Render<SmartOps.Web.Components.Pages.Login>();

            cut.Find("form").Submit();

            Assert.Equal(LoginErrorMapper.EmailRequired, cut.Find("#login-email-error").TextContent);
            Assert.Equal(LoginErrorMapper.PasswordRequired, cut.Find("#login-password-error").TextContent);
            Assert.Empty(JSInterop.Invocations);
            Assert.Empty(cut.FindAll("[role=alert]"));
        }

        [Theory]
        [InlineData(401, LoginErrorMapper.InvalidCredentials)]
        [InlineData(500, LoginErrorMapper.ServerUnavailable)]
        public void ServerResponse_ShowsMappedAlert_AndButtonReturns(int status, string expected)
        {
            JSInterop.Setup<string>("fetchLogin", _ => true).SetResult($"{{\"status\":{status},\"text\":\"\"}}");
            var cut = Render<SmartOps.Web.Components.Pages.Login>();

            cut.Find("#login-email").Change("admin@smartops.com");
            cut.Find("#login-password").Change("wrong");
            cut.Find("form").Submit();

            cut.WaitForAssertion(() => Assert.Equal(expected, cut.Find("[role=alert] span").TextContent));
            Assert.Equal("Ingresar", cut.Find("button[type=submit]").TextContent);
        }

        [Fact]
        public void NetworkFailure_ShowsConnectionMessage()
        {
            JSInterop.Setup<string>("fetchLogin", _ => true).SetException(new Microsoft.JSInterop.JSException("Failed to fetch"));
            var cut = Render<SmartOps.Web.Components.Pages.Login>();

            cut.Find("#login-email").Change("admin@smartops.com");
            cut.Find("#login-password").Change("x");
            cut.Find("form").Submit();

            cut.WaitForAssertion(() => Assert.Equal(LoginErrorMapper.ServerUnavailable, cut.Find("[role=alert] span").TextContent));
        }

        [Fact]
        public void MostrarTogglesPasswordVisibility()
        {
            var cut = Render<SmartOps.Web.Components.Pages.Login>();
            Assert.Equal("password", cut.Find("#login-password").GetAttribute("type"));
            cut.Find(".ds-reveal-btn").Click();
            Assert.Equal("text", cut.Find("#login-password").GetAttribute("type"));
        }
    }
}
