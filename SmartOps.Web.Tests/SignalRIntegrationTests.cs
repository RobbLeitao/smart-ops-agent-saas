using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Xunit;
using SmartOps.Core.Entities;
using SmartOps.Core.Interfaces;
using SmartOps.Core.Events;

namespace SmartOps.Web.Tests
{
    public class SignalRIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public SignalRIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        [Fact(Timeout = 10000)]
        public async Task Hub_Should_Receive_ReceiveTransaction_When_Publisher_Raises_Event()
        {
            // Arrange: create a factory that replaces the ITransactionPublisher with a test publisher
            var customFactory = _factory.WithWebHostBuilder(builder =>
            {
                // Force TransactionSettings:Provider = Noop to avoid starting simulator background service
                builder.ConfigureAppConfiguration((context, cfg) =>
                {
                    var dict = new System.Collections.Generic.Dictionary<string, string>
                    {
                        ["TransactionSettings:Provider"] = "Noop",
                    };
                    cfg.AddInMemoryCollection(dict);
                });

                builder.ConfigureServices(services =>
                {
                    // Remove existing ITransactionPublisher registrations
                    var existing = services.Where(d => d.ServiceType == typeof(ITransactionPublisher)).ToList();
                    foreach (var d in existing) services.Remove(d);

                    // Remove any registered background hosted services (simulator) to avoid timers running in tests
                    var hosted = services.Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)
                        || (d.ImplementationType != null && typeof(Microsoft.Extensions.Hosting.IHostedService).IsAssignableFrom(d.ImplementationType))
                        || (d.ImplementationType != null && d.ImplementationType.Name.Contains("Simulator"))
                        || (d.ImplementationFactory != null && d.ImplementationFactory.Method?.DeclaringType?.Name.Contains("Simulator") == true)
                                            || (d.ServiceType != null && d.ServiceType.Name.Contains("Simulator"))).ToList();
                    foreach (var d in hosted) services.Remove(d);

                    // Register a test publisher that tests can control
                    services.AddSingleton<TestTransactionPublisher>();
                    services.AddSingleton<ITransactionPublisher>(sp => sp.GetRequiredService<TestTransactionPublisher>());
                });
            });

            var publisher = customFactory.Services.GetRequiredService<TestTransactionPublisher>();

            var client = new HubConnectionBuilder()
                .WithUrl(new Uri(customFactory.Server.BaseAddress, "/hubs/transactions"), options =>
                {
                    options.HttpMessageHandlerFactory = _ => customFactory.Server.CreateHandler();
                })
                .Build();

            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            client.On<int>("ReceiveTransaction", id => tcs.TrySetResult(id));

            await client.StartAsync();

            // Act: raise a transaction event
            var tx = new Transaction { Id = 9999, CustomerId = 1, Amount = 1m, Currency = "USD", Status = "Failed", GatewayReference = "TEST", CardLast4 = "1234", Provider = "Test", OccurredAt = DateTime.UtcNow };
            publisher.Raise(tx);

            // Assert: hub received the message
            var receivedId = await Task.WhenAny(tcs.Task, Task.Delay(5000)) == tcs.Task ? tcs.Task.Result : -1;

            await client.DisposeAsync();

            Assert.Equal(tx.Id, receivedId);
        }
    }

    public class TestTransactionPublisher : ITransactionPublisher
    {
        public event EventHandler<TransactionEventArgs>? OnTransactionCreated;

        public void Raise(Transaction tx)
        {
            OnTransactionCreated?.Invoke(this, new TransactionEventArgs(tx));
        }
    }
}
