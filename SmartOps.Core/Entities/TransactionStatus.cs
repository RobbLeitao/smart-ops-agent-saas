using System;

namespace SmartOps.Core.Entities;

/// <summary>
/// Única fuente de verdad para interpretar <see cref="Transaction.Status"/>.
/// El simulador y el seed escriben "Succeeded"; datos de demo viejos usan "Approved" o "Success".
/// Los tres cuentan como aprobada.
/// </summary>
public static class TransactionStatus
{
    public const string Failed = "Failed";
    public const string Succeeded = "Succeeded";

    public static bool IsFailed(string? status) =>
        string.Equals(status, Failed, StringComparison.OrdinalIgnoreCase);

    public static bool IsApproved(string? status) =>
        string.Equals(status, Succeeded, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase);
}
