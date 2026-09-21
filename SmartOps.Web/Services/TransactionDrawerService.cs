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
        public event Action? OnChange;

        public TransactionDto? Current => _current;

        public void Open(TransactionDto dto)
        {
            _current = dto;
            OnChange?.Invoke();
        }

        public void Close()
        {
            _current = null;
            OnChange?.Invoke();
        }
    }
}
