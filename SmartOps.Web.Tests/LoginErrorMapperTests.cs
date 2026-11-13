using SmartOps.Web.Services;
using Xunit;

namespace SmartOps.Web.Tests
{
    public sealed class LoginErrorMapperTests
    {
        [Theory]
        [InlineData(200)]
        [InlineData(204)]
        public void Success_HasNoError(int status) => Assert.Null(LoginErrorMapper.Map(status));

        [Theory]
        [InlineData(401)]
        [InlineData(403)]
        public void InvalidCredentials_DoesNotSayWhichField(int status)
        {
            var message = LoginErrorMapper.Map(status);
            Assert.Equal("El email o la contraseña no son correctos. Probá de nuevo.", message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(400)]
        [InlineData(404)]
        [InlineData(500)]
        [InlineData(502)]
        [InlineData(503)]
        public void ServerOrNetworkError_ShowsConnectionMessage(int? status)
            => Assert.Equal("No pudimos conectar con el servidor. Probá de nuevo en unos segundos.", LoginErrorMapper.Map(status));
    }
}
