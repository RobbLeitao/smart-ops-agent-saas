using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SmartOps.Core.Entities;
using SmartOps.Infrastructure.Data;
using Xunit;

namespace SmartOps.Web.Tests
{
    public sealed class DiagnosticFeedbackPersistenceTests
    {
        [Fact]
        public async Task CanCreateAndRead_IsUsefulTrue()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase("diag-feedback-create-" + Guid.NewGuid())
                .Options;

            using var db = new AppDbContext(options);

            var diag = new Diagnostic
            {
                Id = Guid.NewGuid(),
                TransactionId = 10,
                CreatedAt = DateTime.UtcNow,
                Markdown = "# test",
                CardLast4 = "1234",
                IsUseful = true
            };

            db.Diagnostics.Add(diag);
            await db.SaveChangesAsync();

            var loaded = await db.Diagnostics.FirstOrDefaultAsync(d => d.Id == diag.Id);
            Assert.NotNull(loaded);
            Assert.True(loaded.IsUseful.HasValue && loaded.IsUseful.Value);
        }

        [Fact]
        public async Task CanUpdate_IsUsefulValuePersists()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase("diag-feedback-update-" + Guid.NewGuid())
                .Options;

            using var db = new AppDbContext(options);

            var diag = new Diagnostic
            {
                Id = Guid.NewGuid(),
                TransactionId = 11,
                CreatedAt = DateTime.UtcNow,
                Markdown = "# test update",
                CardLast4 = "5678",
                IsUseful = null
            };

            db.Diagnostics.Add(diag);
            await db.SaveChangesAsync();

            // Update to false
            var toUpdate = await db.Diagnostics.FirstAsync(d => d.Id == diag.Id);
            toUpdate.IsUseful = false;
            await db.SaveChangesAsync();

            var reloaded = await db.Diagnostics.AsNoTracking().FirstAsync(d => d.Id == diag.Id);
            Assert.NotNull(reloaded);
            Assert.True(reloaded.IsUseful.HasValue && reloaded.IsUseful.Value == false);
        }
    }
}
