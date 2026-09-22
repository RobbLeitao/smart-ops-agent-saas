using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SmartOps.Web.Services
{
    public class TransactionDto
    {
        public int Id { get; set; }
        public string? CardLast4 { get; set; }
        public DateTime Timestamp { get; set; }
        public decimal Amount { get; set; }
        public string? Status { get; set; }
        public string? Channel { get; set; }
        public string? ErrorCode { get; set; }
    }

    public class TransactionDrawerService
    {
        private TransactionDto? _current;
        private readonly List<Action<TransactionDto?>> _subscribers = new();
        private readonly object _subsLock = new();
        private readonly ILogger<TransactionDrawerService> _logger;

        public TransactionDto? Current => _current;

        public TransactionDrawerService(ILogger<TransactionDrawerService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInformation("TransactionDrawerService constructed");
        }

        // Register a callback. The callback is invoked immediately with current value.
        public void Register(Action<TransactionDto?> callback)
        {
            if (callback == null) return;
            lock (_subsLock)
            {
                _logger.LogInformation("Register called. CurrentId={Id} SubscribersBefore={Count}", _current?.Id.ToString() ?? "null", _subscribers.Count);
                _subscribers.Add(callback);
            }

            try
            {
                _logger.LogDebug("Invoking newly registered callback immediately with current state. CurrentId={Id}", _current?.Id.ToString() ?? "null");
                callback(_current);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception while invoking registered callback");
            }

            lock (_subsLock)
            {
                _logger.LogInformation("Register completed. SubscribersAfter={Count}", _subscribers.Count);
            }
        }

        public void Unregister(Action<TransactionDto?> callback)
        {
            if (callback == null) return;
            bool removed;
            lock (_subsLock)
            {
                removed = _subscribers.Remove(callback);
                _logger.LogInformation("Unregister called. Removed={Removed} SubscribersNow={Count}", removed, _subscribers.Count);
            }
        }

        private IReadOnlyList<Action<TransactionDto?>> SnapshotSubscribers()
        {
            lock (_subsLock)
            {
                return _subscribers.ToList();
            }
        }

        private void NotifySubscribers()
        {
            var subs = SnapshotSubscribers();
            if (subs.Count == 0)
            {
                _logger.LogWarning("No subscribers when notifying. Starting retry loop to wait for subscribers.");
                // Retry a few times asynchronously to allow interactive component to register
                _ = Task.Run(async () =>
                {
                    for (int i = 1; i <= 10; i++)
                    {
                        await Task.Delay(100);
                        var s2 = SnapshotSubscribers();
                        if (s2.Count > 0)
                        {
                            _logger.LogInformation("Retry notify attempt {Attempt} found {Count} subscribers. Invoking.", i, s2.Count);
                            foreach (var s in s2)
                            {
                                try { s.Invoke(_current); }
                                catch (Exception ex) { _logger.LogError(ex, "Exception while invoking subscriber during retry"); }
                            }
                            return;
                        }
                        else
                        {
                            _logger.LogDebug("Retry notify attempt {Attempt} found no subscribers yet.", i);
                        }
                    }

                    _logger.LogWarning("Retry notify loop ended without subscribers.");
                });
                return;
            }

            foreach (var s in subs)
            {
                try { s.Invoke(_current); }
                catch (Exception ex) { _logger.LogError(ex, "Exception while invoking subscriber"); }
            }
        }

        public void Open(TransactionDto dto)
        {
            _current = dto;
            _logger.LogInformation("Open called. NewId={Id} Subscribers={Count}", _current?.Id.ToString() ?? "null", SnapshotSubscribers().Count);
            if (SnapshotSubscribers().Count == 0)
            {
                _logger.LogWarning("Open called but no subscribers are registered");
            }

            NotifySubscribers();
        }

        public void Close()
        {
            _current = null;
            _logger.LogInformation("Close called. Subscribers={Count}", SnapshotSubscribers().Count);
            NotifySubscribers();
        }
    }
}

