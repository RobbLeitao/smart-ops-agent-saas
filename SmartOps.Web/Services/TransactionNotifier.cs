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

        // Subscribe to publisher events
        _publisher.OnTransactionCreated += Publisher_OnTransactionCreated;
    }

    private void Publisher_OnTransactionCreated(object? sender, TransactionEventArgs e)
    {
        // Fire-and-forget send; log exceptions
        Task.Run(async () =>
        {
            try
            {
                await _hubContext.Clients.All.SendAsync("ReceiveTransaction", e.Transaction);
                _logger.LogInformation("Notified clients about transaction {Id}", e.Transaction.Id);
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
