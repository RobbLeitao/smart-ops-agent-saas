using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SmartOps.Core.Entities;
using SmartOps.Infrastructure.Data;
using Xunit;

namespace SmartOps.Web.Tests;

public sealed class DiagnosticOrchestratorServiceTests
{
    private sealed class FakeAIOpsService : SmartOps.Web.Services.IAIOpsService
    {
        public string? LastPrompt { get; private set; }

        public Task<string> ExecutePromptAsync(string userPrompt)
        {
            LastPrompt = userPrompt;
            var md = "## 🔍 Resumen del Error\nDetalle del error\n\n## 🛠️ Acciones Recomendadas para el Operador\n- Revisar logs\n\n## 📨 Plantilla de Correo para el Cliente\nEstimado cliente...";
            return Task.FromResult(md);
        }
    }

    [Fact]
    public async Task RunDiagnosticAsync_ReturnsStructuredMarkdown()
    {
        var transactionId = Guid.NewGuid();
        var fake = new FakeAIOpsService();
        var svc = new SmartOps.Web.Services.DiagnosticOrchestratorService(fake);

        var result = await svc.RunDiagnosticAsync(transactionId);

        // Ensure the orchestrator injected the transactionId into the prompt
        Assert.NotNull(fake.LastPrompt);
        Assert.Contains(transactionId.ToString(), fake.LastPrompt);

        Assert.Contains("## 🔍 Resumen del Error", result);
        Assert.Contains("## 🛠️ Acciones Recomendadas para el Operador", result);
        Assert.Contains("## 📨 Plantilla de Correo para el Cliente", result);
    }

    [Fact]
    public async Task RunDiagnosticAsync_PersistsTransactionCardLast4()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("diagnostic-card-" + Guid.NewGuid())
            .Options;

        using var db = new AppDbContext(options);
        db.Transactions.Add(new Transaction
        {
            Id = 2,
            CustomerId = 1,
            Amount = 49m,
            Currency = "USD",
            Status = "Failed",
            GatewayReference = "TXN_ERR_502",
            CardLast4 = "4242",
            Provider = "Stripe",
            ErrorMessage = "Card declined",
            OccurredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var fake = new FakeAIOpsService();
        var svc = new SmartOps.Web.Services.DiagnosticOrchestratorService(fake, db);
        var tx = await db.Transactions.FirstAsync();

        await svc.RunDiagnosticAsync(tx);

        var saved = await db.Diagnostics.FirstAsync();
        Assert.Equal("4242", saved.CardLast4);
        Assert.Equal(tx.Id, saved.TransactionId);
    }
}
