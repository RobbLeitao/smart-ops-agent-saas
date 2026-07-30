using System;

namespace SmartOps.Core.Entities;

public class DiagnosticFeedback
{
    public Guid Id { get; set; }
    public Guid DiagnosticId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public bool IsUseful { get; set; }
    public DateTime VotedAt { get; set; } = DateTime.UtcNow;
}
