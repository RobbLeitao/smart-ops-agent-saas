namespace SmartOps.Web.Services
{
    /// <summary>
    /// Maps the outcome of POST /account/login to the message shown in the Login alert
    /// (docs/redesign/06-login.html). Invalid credentials never say which of the two fields failed.
    /// </summary>
    public static class LoginErrorMapper
    {
        public const string InvalidCredentials = "El email o la contraseña no son correctos. Probá de nuevo.";
        public const string ServerUnavailable = "No pudimos conectar con el servidor. Probá de nuevo en unos segundos.";
        public const string EmailRequired = "Ingresá tu email.";
        public const string PasswordRequired = "Ingresá tu contraseña.";

        /// <param name="status">HTTP status of the response; null when the request never got one (network error).</param>
        /// <returns>Null on success; otherwise the alert message.</returns>
        public static string? Map(int? status) => status switch
        {
            >= 200 and <= 299 => null,
            401 or 403 => InvalidCredentials,
            _ => ServerUnavailable
        };
    }
}
