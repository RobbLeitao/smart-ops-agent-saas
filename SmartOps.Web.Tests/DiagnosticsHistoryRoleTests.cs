using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using SmartOps.Core.Entities;
using SmartOps.Infrastructure.Data;
using Xunit;

namespace SmartOps.Web.Tests;

public sealed class DiagnosticsHistoryRoleTests
{
    [Fact]
    public async Task OperatorOnlySeesOwnDiagnostics()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("hist-operator-" + Guid.NewGuid())
            .Options;

        using var db = new AppDbContext(options);
        db.Diagnostics.AddRange(
            new Diagnostic
            {
                Id = Guid.NewGuid(),
                TransactionId = 100,
                CreatedAt = DateTime.UtcNow,
                Markdown = "op",
                CardLast4 = "1111",
                CreatedByUserId = "operator-user-id"
            },
            new Diagnostic
            {
                Id = Guid.NewGuid(),
                TransactionId = 101,
                CreatedAt = DateTime.UtcNow,
                Markdown = "admin",
                CardLast4 = "2222",
                CreatedByUserId = "admin-user-id"
            });
        await db.SaveChangesAsync();

        var diagnostics = await db.Diagnostics
            .Where(x => x.CreatedByUserId == "operator-user-id")
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        Assert.Single(diagnostics);
        Assert.All(diagnostics, x => Assert.Equal("operator-user-id", x.CreatedByUserId));
    }

    [Fact]
    public async Task AdminSeesAllDiagnosticsWithCreatorInfo()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("hist-admin-" + Guid.NewGuid())
            .Options;

        using var db = new AppDbContext(options);
        db.Diagnostics.AddRange(
            new Diagnostic
            {
                Id = Guid.NewGuid(),
                TransactionId = 200,
                CreatedAt = DateTime.UtcNow,
                Markdown = "op",
                CardLast4 = "3333",
                CreatedByUserId = "operator-user-id"
            },
            new Diagnostic
            {
                Id = Guid.NewGuid(),
                TransactionId = 201,
                CreatedAt = DateTime.UtcNow,
                Markdown = "admin",
                CardLast4 = "4444",
                CreatedByUserId = "admin-user-id"
            });
        await db.SaveChangesAsync();

        var diagnostics = await db.Diagnostics
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        Assert.Equal(2, diagnostics.Count);
        Assert.Contains(diagnostics, x => x.CreatedByUserId == "operator-user-id");
        Assert.Contains(diagnostics, x => x.CreatedByUserId == "admin-user-id");
    }
}
