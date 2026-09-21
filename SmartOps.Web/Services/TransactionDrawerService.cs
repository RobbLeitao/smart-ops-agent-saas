using System;
using System.Threading.Tasks;

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

        public TransactionDto? Current => _current;

        // Register a callback. The callback is invoked immediately with current value.
        public void Register(Action<TransactionDto?> callback)
        {
            if (callback == null) return;
            _subscribers.Add(callback);
            callback(_current);
        }

        public void Unregister(Action<TransactionDto?> callback)
        {
            if (callback == null) return;
            _subscribers.Remove(callback);
        }

        public void Open(TransactionDto dto)
        {
            _current = dto;
            foreach (var s in _subscribers.ToList())
            {
                try { s.Invoke(_current); } catch { }
            }
        }

        public void Close()
        {
            _current = null;
            foreach (var s in _subscribers.ToList())
            {
                try { s.Invoke(_current); } catch { }
            }
        }
    }
}
