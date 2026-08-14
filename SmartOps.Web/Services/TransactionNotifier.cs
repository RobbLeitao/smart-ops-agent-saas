using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SmartOps.Core.Events;
using SmartOps.Core.Interfaces;
using SmartOps.Web.Hubs;

namespace SmartOps.Web.Services;

public class TransactionNotifier : IDisposable
{
    private readonly IHubContext<TransactionHub> _hubContext;
    private readonly ITransactionPublisher _publisher;
    private readonly ILogger<TransactionNotifier> _logger;

    public TransactionNotifier(IHubContext<TransactionHub> hubContext, ITransactionPublisher publisher, ILogger<TransactionNotifier> logger)
    {
        _hubContext = hubContext;
        _publisher = publisher;
        _logger = logger;

            _logger.LogInformation("TransactionNotifier constructed. Publisher is {HasPublisher}", _publisher != null);

            // Subscribe to publisher events
            _publisher.OnTransactionCreated += Publisher_OnTransactionCreated;

            _logger.LogInformation("TransactionNotifier subscribed to publisher events.");
        }

    private void Publisher_OnTransactionCreated(object? sender, TransactionEventArgs e)
    {
        // Fire-and-forget send; log exceptions
        Task.Run(async () =>
        {
            try
            {
                // Send only the primitive ID to avoid JSON serialization errors with EF entity graphs
                var txId = e.Transaction.Id;
                await _hubContext.Clients.All.SendAsync("ReceiveTransaction", txId);
                _logger.LogInformation("Notified clients about transaction {Id}", txId);

                await _hubContext.Clients.All.SendAsync("NotificationCreated", txId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to notify clients for transaction {Id}", e.Transaction.Id);
            }
        });
    }

    public void Dispose()
    {
        try
        {
            _publisher.OnTransactionCreated -= Publisher_OnTransactionCreated;
        }
        catch { }
    }
}
