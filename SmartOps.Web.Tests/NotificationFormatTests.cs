using System;
using System.Globalization;
using SmartOps.Web.Services;
using Xunit;

namespace SmartOps.Web.Tests
{
    public sealed class NotificationFormatTests
    {
        [Theory]
        [InlineData(0, null)]
        [InlineData(5, "5")]
        [InlineData(99, "99")]
        [InlineData(100, "99+")]
        [InlineData(1013, "99+")]
        public void Badge_CapsAt99(int unread, string? expected) => Assert.Equal(expected, NotificationFormat.Badge(unread));

        [Fact]
        public void AriaLabel_AlwaysHasExactNumber()
        {
            Assert.Equal("Notificaciones, 495 sin leer", NotificationFormat.AriaLabel(495));
            Assert.Equal("Notificaciones, ninguna sin leer", NotificationFormat.AriaLabel(0));
        }

        private static readonly DateTime Now = new(2026, 11, 13, 20, 0, 0, DateTimeKind.Utc);

        [Theory]
        [InlineData(0, "ahora")]
        [InlineData(59, "ahora")]
        [InlineData(60, "hace 1 min")]
        [InlineData(59 * 60 + 59, "hace 59 min")]
        [InlineData(3600, "hace 1 h")]
        [InlineData(23 * 3600 + 3599, "hace 23 h")]
        public void RelativeTime_Recent(int secondsAgo, string expected)
            => Assert.Equal(expected, NotificationFormat.RelativeTime(Now.AddSeconds(-secondsAgo), Now));

        [Fact]
        public void RelativeTime_After24h_IsShortLocalDate()
        {
            var created = Now.AddHours(-30);
            var expected = created.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
            Assert.Equal(expected, NotificationFormat.RelativeTime(created, Now));
        }

        [Fact]
        public void RelativeTime_UnspecifiedKind_IsTreatedAsUtc()
        {
            var sqlite = DateTime.SpecifyKind(Now.AddMinutes(-5), DateTimeKind.Unspecified);
            Assert.Equal("hace 5 min", NotificationFormat.RelativeTime(sqlite, Now));
        }
    }
}
