using System;

namespace SmartOps.Core.Entities
{
    public class Notification
    {
        public int Id { get; set; }
        public int? TransactionId { get; set; }
        public string? Title { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Type { get; set; } = "Info"; // Error/Warning/Info
        public bool IsRead { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}