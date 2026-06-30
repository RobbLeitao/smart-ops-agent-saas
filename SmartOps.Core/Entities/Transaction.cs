namespace SmartOps.Core.Entities;

public class Transaction
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string GatewayReference { get; set; } = string.Empty;

    // New fields for provider, error message and occurrence timestamp
    public string Provider { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTime? OccurredAt { get; set; }
}
