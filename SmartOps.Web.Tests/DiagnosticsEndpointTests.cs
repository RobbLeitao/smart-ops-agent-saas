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

        [Fact]
        public async Task Get_Diagnostics_Returns_Markdown_When_Found()
        {
            var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(b =>
                {
                    b.ConfigureServices(services =>
                    {
                        // Override IAIOpsService with fake
                        services.AddScoped<SmartOps.Web.Services.IAIOpsService, FakeAIOpsService>();

                        // Remove existing AppDbContext registrations (Sqlite) so we can replace with InMemory for tests
                        var descriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>) || d.ServiceType == typeof(AppDbContext)).ToList();
                        foreach (var d in descriptors) services.Remove(d);

                        // Use in-memory DB for testing
                        services.AddDbContext<AppDbContext>(options =>
                            options.UseInMemoryDatabase("DiagnosticsEndpointDb" + Guid.NewGuid()));
                    });
                });

            using var client = factory.CreateClient();

            var txId = Guid.NewGuid();

            var response = await client.GetAsync($"/api/diagnostics/{txId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/markdown", response.Content.Headers.ContentType?.MediaType);

            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("## 🔍 Resumen del Error", content);
        }
    }
}
