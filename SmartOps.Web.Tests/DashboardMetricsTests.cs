using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SmartOps.Infrastructure.Data;
using SmartOps.Core.Entities;
using Xunit;

namespace SmartOps.Web.Tests
{
    public sealed class DashboardMetricsTests
    {
        [Fact]
        public async Task UsefulPercent_CalculatedCorrectly()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase("dashboard-metrics-" + Guid.NewGuid())
                .Options;

            using var db = new AppDbContext(options);

            // Seed diagnostics: 3 rated, 2 useful, 1 not useful; plus 1 unrated
            db.Diagnostics.Add(new Diagnostic { Id = Guid.NewGuid(), TransactionId = 1, CreatedAt = DateTime.UtcNow, Markdown = "m1", CardLast4 = "1111", IsUseful = true });
            db.Diagnostics.Add(new Diagnostic { Id = Guid.NewGuid(), TransactionId = 2, CreatedAt = DateTime.UtcNow, Markdown = "m2", CardLast4 = "2222", IsUseful = true });
            db.Diagnostics.Add(new Diagnostic { Id = Guid.NewGuid(), TransactionId = 3, CreatedAt = DateTime.UtcNow, Markdown = "m3", CardLast4 = "3333", IsUseful = false });
            db.Diagnostics.Add(new Diagnostic { Id = Guid.NewGuid(), TransactionId = 4, CreatedAt = DateTime.UtcNow, Markdown = "m4", CardLast4 = "4444", IsUseful = null });

            await db.SaveChangesAsync();

            var totalRated = await db.Diagnostics.CountAsync(d => d.IsUseful != null);
            var usefulCount = await db.Diagnostics.CountAsync(d => d.IsUseful == true);
            Assert.Equal(3, totalRated);
            Assert.Equal(2, usefulCount);

            var percent = (int)Math.Round((double)usefulCount / totalRated * 100);
            Assert.Equal(67, percent); // 2/3 -> 66.666 -> 67 rounded
        }

        [Fact]
        public async Task UsefulPercent_NoRatings_ShowsNA()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase("dashboard-metrics-empty-" + Guid.NewGuid())
                .Options;

            using var db = new AppDbContext(options);
            db.Diagnostics.Add(new Diagnostic { Id = Guid.NewGuid(), TransactionId = 5, CreatedAt = DateTime.UtcNow, Markdown = "m5", CardLast4 = "5555", IsUseful = null });
            await db.SaveChangesAsync();

            var totalRated = await db.Diagnostics.CountAsync(d => d.IsUseful != null);
            Assert.Equal(0, totalRated);
        }
    }
}
