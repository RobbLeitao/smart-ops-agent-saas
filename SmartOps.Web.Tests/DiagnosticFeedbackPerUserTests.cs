using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SmartOps.Core.Entities;
using SmartOps.Infrastructure.Data;
using Xunit;

namespace SmartOps.Web.Tests;

public sealed class DiagnosticFeedbackPerUserTests
{
    [Fact]
    public async Task CanStoreFeedbackForSameDiagnosticFromDifferentUsers()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("diag-feedback-per-user-" + Guid.NewGuid())
            .Options;

        using var db = new AppDbContext(options);

        var diagnosticId = Guid.NewGuid();
        db.Diagnostics.Add(new Diagnostic
        {
            Id = diagnosticId,
            TransactionId = 42,
            CreatedAt = DateTime.UtcNow,
            Markdown = "m",
            CardLast4 = "1234"
        });

        db.DiagnosticFeedback.AddRange(
            new DiagnosticFeedback
            {
                Id = Guid.NewGuid(),
                DiagnosticId = diagnosticId,
                UserId = "operator-user-id",
                IsUseful = true,
                VotedAt = DateTime.UtcNow
            },
            new DiagnosticFeedback
            {
                Id = Guid.NewGuid(),
                DiagnosticId = diagnosticId,
                UserId = "admin-user-id",
                IsUseful = false,
                VotedAt = DateTime.UtcNow
            });

        await db.SaveChangesAsync();

        var votes = await db.DiagnosticFeedback
            .Where(x => x.DiagnosticId == diagnosticId)
            .ToListAsync();

        Assert.Equal(2, votes.Count);
        Assert.Contains(votes, x => x.UserId == "operator-user-id" && x.IsUseful);
        Assert.Contains(votes, x => x.UserId == "admin-user-id" && !x.IsUseful);
    }

    [Fact]
    public async Task CanReadExistingVoteForSpecificUserOnly()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("diag-feedback-read-" + Guid.NewGuid())
            .Options;

        using var db = new AppDbContext(options);

        var diagnosticId = Guid.NewGuid();
        db.Diagnostics.Add(new Diagnostic
        {
            Id = diagnosticId,
            TransactionId = 43,
            CreatedAt = DateTime.UtcNow,
            Markdown = "m",
            CardLast4 = "5678"
        });

        db.DiagnosticFeedback.Add(new DiagnosticFeedback
        {
            Id = Guid.NewGuid(),
            DiagnosticId = diagnosticId,
            UserId = "operator-user-id",
            IsUseful = true,
            VotedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var operatorVote = await db.DiagnosticFeedback
            .SingleOrDefaultAsync(x => x.DiagnosticId == diagnosticId && x.UserId == "operator-user-id");
        var adminVote = await db.DiagnosticFeedback
            .SingleOrDefaultAsync(x => x.DiagnosticId == diagnosticId && x.UserId == "admin-user-id");

        Assert.NotNull(operatorVote);
        Assert.True(operatorVote!.IsUseful);
        Assert.Null(adminVote);
    }

    [Fact]
    public async Task DuplicateVoteForSameUserAndDiagnosticIsRejectedByUniqueIndex()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=file:diag-feedback-unique-" + Guid.NewGuid() + "?mode=memory&cache=shared")
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();

        var diagnosticId = Guid.NewGuid();
        db.Diagnostics.Add(new Diagnostic
        {
            Id = diagnosticId,
            TransactionId = 44,
            CreatedAt = DateTime.UtcNow,
            Markdown = "m",
            CardLast4 = "9012"
        });
        await db.SaveChangesAsync();

        db.DiagnosticFeedback.Add(new DiagnosticFeedback
        {
            Id = Guid.NewGuid(),
            DiagnosticId = diagnosticId,
            UserId = "operator-user-id",
            IsUseful = true,
            VotedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        db.DiagnosticFeedback.Add(new DiagnosticFeedback
        {
            Id = Guid.NewGuid(),
            DiagnosticId = diagnosticId,
            UserId = "operator-user-id",
            IsUseful = false,
            VotedAt = DateTime.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
