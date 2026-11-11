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

    private sealed class ThrowingAIOpsService(Exception toThrow) : SmartOps.Web.Services.IAIOpsService
    {
        public Task<string> ExecutePromptAsync(string userPrompt) => Task.FromException<string>(toThrow);
    }

    private static async Task<(AppDbContext Db, Transaction Tx)> CreateDbWithFailedTransactionAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("diagnostic-ai-error-" + Guid.NewGuid())
            .Options;

        var db = new AppDbContext(options);
        db.Transactions.Add(new Transaction
        {
            Id = 7,
            CustomerId = 1,
            Amount = 49m,
            Currency = "USD",
            Status = "Failed",
            GatewayReference = "TXN_ERR_7",
            CardLast4 = "4242",
            Provider = "Stripe",
            ErrorMessage = "Card declined",
            OccurredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return (db, await db.Transactions.FirstAsync());
    }

    [Fact]
    public async Task RunDiagnosticAsync_AiEngineException_PropagatesWithoutPersistingDiagnostic()
    {
        var (db, tx) = await CreateDbWithFailedTransactionAsync();
        using var _ = db;
        var aiError = new SmartOps.Web.Services.AiEngineException("La API Key configurada en /integrations no es válida.");
        var svc = new SmartOps.Web.Services.DiagnosticOrchestratorService(new ThrowingAIOpsService(aiError), db);

        var ex = await Assert.ThrowsAsync<SmartOps.Web.Services.AiEngineException>(() => svc.RunDiagnosticAsync(tx, "user-1"));

        Assert.Same(aiError, ex);
        Assert.Equal(0, await db.Diagnostics.CountAsync());
    }

    [Fact]
    public async Task RunDiagnosticAsync_ByGuid_AiEngineException_Propagates()
    {
        var svc = new SmartOps.Web.Services.DiagnosticOrchestratorService(
            new ThrowingAIOpsService(new SmartOps.Web.Services.AiEngineException("rate limit")));

        await Assert.ThrowsAsync<SmartOps.Web.Services.AiEngineException>(() => svc.RunDiagnosticAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task RunDiagnosticAsync_NonAiException_KeepsExistingFallbackWithoutPersisting()
    {
        var (db, tx) = await CreateDbWithFailedTransactionAsync();
        using var _ = db;
        var svc = new SmartOps.Web.Services.DiagnosticOrchestratorService(
            new ThrowingAIOpsService(new InvalidOperationException("unexpected")), db);

        var result = await svc.RunDiagnosticAsync(tx);

        Assert.Contains("No se pudo ejecutar el proveedor de IA", result);
        Assert.Equal(0, await db.Diagnostics.CountAsync());
    }
}
