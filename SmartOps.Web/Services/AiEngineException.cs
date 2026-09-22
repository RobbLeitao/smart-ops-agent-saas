using System;

namespace SmartOps.Web.Services
{
    /// <summary>
    /// Thrown by real AI engine adapters (e.g. <see cref="AzureOpenAiDiagnosticEngine"/>) when the
    /// underlying provider call fails. The <see cref="Exception.Message"/> is a clear, operator-facing
    /// description that the UI can display directly, without leaking SDK-internal details.
    /// </summary>
    public sealed class AiEngineException : Exception
    {
        public AiEngineException(string message)
            : base(message)
        {
        }

        public AiEngineException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
