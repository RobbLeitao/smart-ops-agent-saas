using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartOps.Core.Entities;
using SmartOps.Core.Events;
using SmartOps.Core.Interfaces;
using SmartOps.Infrastructure.Data;

namespace SmartOps.Infrastructure;

public class TransactionSettings
{
    public int IntervalSeconds { get; set; } = 8;
    public string Provider { get; set; } = "Simulator";
}

public class SimulatorTransactionService : BackgroundService, ITransactionPublisher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SimulatorTransactionService> _logger;
    private readonly IOptions<TransactionSettings> _settings;
    private Timer? _timer;

    public event EventHandler<TransactionEventArgs>? OnTransactionCreated;

    public SimulatorTransactionService(IServiceScopeFactory scopeFactory, ILogger<SimulatorTransactionService> logger, IOptions<TransactionSettings> settings)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settings = settings;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _settings.Value.IntervalSeconds));
        _logger.LogInformation("SimulatorTransactionService starting with interval {Interval}s", interval.TotalSeconds);
        _timer = new Timer(Callback, null, TimeSpan.Zero, interval);
        return Task.CompletedTask;
    }

    private void Callback(object? state)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var rnd = new Random();
            var status = rnd.NextDouble() > 0.6 ? "Succeeded" : "Failed";
            string? error = null;
            if (status == "Failed")
            {
                var errors = new[] { "Card declined", "Insufficient funds", "Expired card", "Suspected fraud" };
                error = errors[rnd.Next(errors.Length)];
            }

            var nextId = db.Transactions.Any() ? db.Transactions.Max(t => t.Id) + 1 : 1;
            var tx = new Transaction
            {
                Id = nextId,
                CustomerId = 1,
                Amount = Math.Round((decimal)(10 + rnd.NextDouble() * 100), 2),
                Currency = rnd.Next(2) == 0 ? "USD" : "EUR",
                Status = status,
                GatewayReference = $"SIM_{Guid.NewGuid().ToString().Split('-')[0].ToUpperInvariant()}",
                CardLast4 = (1000 + rnd.Next(9000)).ToString("D4"),
                Provider = "Simulator",
                ErrorMessage = error,
                OccurredAt = DateTime.UtcNow
            };

            db.Transactions.Add(tx);
            db.SaveChanges();

            // Logging may fail in certain test runners (EventLog disposed). Swallow logging exceptions to avoid crashing tests.
            try
            {
                try
                {
                    _logger.LogInformation("Simulator created transaction {Id} status={Status}", tx.Id, tx.Status);
                }
                catch
                {
                    // ignore logging errors
                }

                try
                {
                    // If transaction failed, persist a Notification
                    if (string.Equals(tx.Status, "Failed", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var notif = new SmartOps.Core.Entities.Notification
                            {
                                TransactionId = tx.Id,
                                Title = $"Transacción {tx.Id} falló",
                                Message = tx.ErrorMessage ?? "",
                                Type = "Error",
                                IsRead = false,
                                CreatedAt = DateTime.UtcNow
                            };
                            db.Notifications.Add(notif);
                            db.SaveChanges();
                            _logger.LogInformation("Persisted notification for transaction {Id}", tx.Id);
                        }
                        catch (Exception ex)
                        {
                            try { _logger.LogError(ex, "Failed to persist notification for transaction {Id}", tx.Id); } catch { }
                        }
                    }

                    _logger.LogInformation("[DI-PROBE] Simulator about to invoke OnTransactionCreated for {Id}", tx.Id);
                    OnTransactionCreated?.Invoke(this, new TransactionEventArgs(tx));
                    _logger.LogInformation("[DI-PROBE] Simulator invoked OnTransactionCreated for {Id}", tx.Id);
                }
                catch (Exception ex)
                {
                    try { _logger.LogError(ex, "Error invoking OnTransactionCreated"); } catch { }
                }
            }
            catch
            {
                // ensure no exception propagates from logging/event invocation
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Simulator transaction creation failed");
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return base.StopAsync(cancellationToken);
    }
}
