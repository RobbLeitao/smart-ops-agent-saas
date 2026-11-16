namespace SmartOps.Web.Services;

/// <summary>
/// The transaction to diagnose does not exist. It is an <see cref="AiEngineException"/> so every caller that already
/// shows operator-facing AI errors keeps working; the webhook maps it to 404 instead of 502.
/// </summary>
public sealed class TransactionNotFoundException : AiEngineException
{
    public const string DefaultMessage = "No se encontró la transacción indicada.";

    public TransactionNotFoundException() : base(DefaultMessage)
    {
    }
}
