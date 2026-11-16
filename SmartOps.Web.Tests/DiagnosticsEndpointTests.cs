using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using SmartOps.Infrastructure.Data;
using Xunit;

namespace SmartOps.Web.Tests
{
    public class DiagnosticsEndpointTests
    {
        private sealed class FakeAIOpsService : SmartOps.Web.Services.IAIOpsService
        {
            public string? LastPrompt { get; private set; }

            public Task<string> ExecutePromptAsync(string userPrompt)
            {
                LastPrompt = userPrompt;
                // Return a markdown response that includes the transaction id if present
                return Task.FromResult("## 🔍 Resumen del Error\nDetalle\n\n## 🛠️ Acciones Recomendadas para el Operador\n- Acción\n\n## 📨 Plantilla de Correo para el Cliente\nEstimado cliente...");
            }
        }


        private static WebApplicationFactory<Program> Factory<TAi>(string dbName) where TAi : class, SmartOps.Web.Services.IAIOpsService
            => new WebApplicationFactory<Program>()
                .WithWebHostBuilder(b =>
                {
                    b.ConfigureServices(services =>
                    {
                        services.AddScoped<SmartOps.Web.Services.IAIOpsService, TAi>();

                        // EF Core 9 also keeps the Sqlite configuration in IDbContextOptionsConfiguration<AppDbContext>.
                        var descriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                            || d.ServiceType == typeof(AppDbContext)
                            || d.ServiceType == typeof(Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<AppDbContext>)).ToList();
                        foreach (var d in descriptors) services.Remove(d);

                        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName));
                    });
                });

        // Failed transaction with a known Id; returns the Guid the webhook uses for it.
        private static async Task<Guid> SeedTransactionAsync(WebApplicationFactory<Program> factory, int id = 900001)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Transactions.Add(new SmartOps.Core.Entities.Transaction
            {
                Id = id, CustomerId = 1, Amount = 49m, Currency = "USD", Status = "Failed", GatewayReference = "ch_test",
                CardLast4 = "4242", Provider = "Stripe", ErrorMessage = "Card declined", OccurredAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var ns = System.Text.Encoding.UTF8.GetBytes("SmartOps.Transaction.IntMapping");
            using var md5 = System.Security.Cryptography.MD5.Create();
            var hash = md5.ComputeHash(ns.Concat(System.Text.Encoding.UTF8.GetBytes(id.ToString())).ToArray());
            return new Guid(hash.Take(16).ToArray());
        }

        private static int CountDiagnostics(WebApplicationFactory<Program> factory)
        {
            using var scope = factory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<AppDbContext>().Diagnostics.Count();
        }

        [Fact]
        public async Task Get_Diagnostics_Returns_Markdown_When_Found()
        {
            using var factory = Factory<FakeAIOpsService>("DiagnosticsEndpointDb" + Guid.NewGuid());
            using var client = factory.CreateClient();
            var txId = await SeedTransactionAsync(factory);

            // Stripe-like JSON payload with transactionId in data.object.metadata.transactionId
            var payload = new
            {
                type = "charge.failed",
                data = new
                {
                    @object = new
                    {
                        id = "evt_123",
                        metadata = new
                        {
                            transactionId = txId.ToString()
                        }
                    }
                }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"/api/webhooks/stripe", content);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/markdown", response.Content.Headers.ContentType?.MediaType);

            var responseBody = await response.Content.ReadAsStringAsync();
            Assert.Contains("## 🔍 Resumen del Error", responseBody);
        }

        [Fact]
        public async Task StripeWebhook_UnknownTransaction_Returns404WithMessage_AndPersistsNothing()
        {
            using var factory = Factory<FakeAIOpsService>("DiagnosticsEndpoint404Db" + Guid.NewGuid());
            using var client = factory.CreateClient();
            var before = CountDiagnostics(factory);

            var response = await client.PostAsync($"/api/webhooks/stripe/{Guid.NewGuid()}", new StringContent(string.Empty));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("No se encontró la transacción indicada.", await response.Content.ReadAsStringAsync());
            Assert.Equal(before, CountDiagnostics(factory));
        }

        private sealed class FailingAIOpsService : SmartOps.Web.Services.IAIOpsService
        {
            public const string Message = "La API Key configurada en /integrations no es válida o no tiene permisos para este modelo. Verificá las credenciales.";

            public Task<string> ExecutePromptAsync(string userPrompt)
                => Task.FromException<string>(new SmartOps.Web.Services.AiEngineException(Message));
        }

        [Fact]
        public async Task StripeWebhook_AiEngineException_Returns502WithOperatorMessage()
        {
            using var factory = Factory<FailingAIOpsService>("DiagnosticsEndpoint502Db" + Guid.NewGuid());
            using var client = factory.CreateClient();
            var txId = await SeedTransactionAsync(factory);

            var response = await client.PostAsync($"/api/webhooks/stripe/{txId}", new StringContent(string.Empty));

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Equal(FailingAIOpsService.Message, await response.Content.ReadAsStringAsync());
        }
    }
}
