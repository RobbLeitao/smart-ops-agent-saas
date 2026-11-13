using System;

namespace SmartOps.Web.Services
{
    /// <summary>
    /// Validates the optional custom endpoint used by the "OpenAI" provider to target an OpenAI-compatible API
    /// (e.g. Groq: https://api.groq.com/openai/v1). Shared by the /integrations UI and the chat completion factory.
    /// Rules: empty is valid (official OpenAI endpoint); otherwise an absolute https URI, or http only for
    /// localhost / 127.0.0.1 / [::1] (local models).
    /// </summary>
    public static class OpenAICompatibleEndpointValidator
    {
        public const string InvalidUriMessage =
            "El endpoint debe ser una URL absoluta válida (por ejemplo https://api.groq.com/openai/v1).";

        public const string HttpNotAllowedMessage =
            "El endpoint debe usar https. Solo se permite http para localhost o 127.0.0.1.";

        /// <summary>
        /// Returns true when <paramref name="value"/> is empty or a valid endpoint. <paramref name="endpoint"/> is
        /// null for an empty value. <paramref name="error"/> is an operator-facing message when invalid.
        /// </summary>
        public static bool TryValidate(string? value, out Uri? endpoint, out string? error)
        {
            endpoint = null;
            error = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                || string.IsNullOrEmpty(uri.Host))
            {
                error = InvalidUriMessage;
                return false;
            }

            if (uri.Scheme == Uri.UriSchemeHttp && !IsLoopbackHost(uri))
            {
                error = HttpNotAllowedMessage;
                return false;
            }

            endpoint = uri;
            return true;
        }

        private static bool IsLoopbackHost(Uri uri)
            => uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || uri.Host == "127.0.0.1"
               || uri.Host == "[::1]";
    }
}
