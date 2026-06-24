namespace SmartOps.Core.Entities;

public class OperationLog
{
    public int Id { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Component { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
