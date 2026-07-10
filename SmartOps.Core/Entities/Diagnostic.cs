using System;

namespace SmartOps.Core.Entities
{
    public class Diagnostic
    {
        public Guid Id { get; set; }
        public int TransactionId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Markdown { get; set; } = string.Empty;

        // Lightweight searchable fields
        public string? Summary { get; set; }
        public string? SuggestedMessage { get; set; }

        // Last 4 digits of card at time of transaction (stored for quick filtering)
        public string? CardLast4 { get; set; }
    }
}
