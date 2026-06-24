namespace SmartOps.Core.Entities;

public class Transaction
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string GatewayReference { get; set; } = string.Empty;
}
