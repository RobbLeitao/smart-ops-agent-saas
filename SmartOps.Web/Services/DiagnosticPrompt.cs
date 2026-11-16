using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using SmartOps.Core.Entities;

namespace SmartOps.Web.Services;

/// <summary>
/// Prompt de diagnóstico listo para enviar: mensaje de sistema + mensaje de usuario con los datos de la transacción.
/// <see cref="Transaction"/> viaja aparte para el Simulador local, que no lee texto sino los datos.
/// </summary>
public sealed record DiagnosticPrompt(string Version, string System, string User, Transaction Transaction)
{
    /// <summary>Monto como lo ve el cliente (por ejemplo «US$ 98,54»). Es el mismo texto que va en el prompt.</summary>
    public string FormattedAmount => DiagnosticFormatting.FormatAmount(Transaction.Amount, Transaction.Currency);

    /// <summary>Tarjeta como la ve el cliente («terminada en 1234»).</summary>
    public string CardText => DiagnosticFormatting.FormatCard(Transaction.CardLast4);
}

/// <summary>
/// Resultado del motor de diagnóstico. <see cref="PromptVersion"/> se guarda en Diagnostics para medir la
/// efectividad por versión: «v2» (LLM con diagnostic.v2.md), «sim-v2» (Simulador local), null = anterior («v1»).
/// </summary>
public sealed record AiDiagnosticResult(string Markdown, string? PromptVersion);

/// <summary>Formato de monto y tarjeta compartido por el prompt y el Simulador local.</summary>
public static class DiagnosticFormatting
{
    // Formato es-AR fijo (98,54 / 1.234,50), sin depender de ICU ni de la cultura del servidor.
    private static readonly NumberFormatInfo EsAr = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberGroupSizes = new[] { 3 },
        NegativeSign = "-"
    };

    /// <summary>ARS → «$ 98,54», USD → «US$ 98,54», otra moneda → «EUR 98,54».</summary>
    public static string FormatAmount(decimal amount, string? currency)
    {
        var number = amount.ToString("N2", EsAr);
        var code = (currency ?? string.Empty).Trim().ToUpperInvariant();
        var symbol = code switch
        {
            "ARS" => "$",
            "USD" => "US$",
            "" => null,
            _ => code
        };

        return symbol is null ? number : $"{symbol} {number}";
    }

    public static string FormatCard(string? last4)
        => string.IsNullOrWhiteSpace(last4) ? "no informada" : $"terminada en {last4.Trim()}";
}

/// <summary>
/// Arma el prompt v2 desde el recurso embebido Prompts/diagnostic.v2.md. Los campos de la transacción se insertan
/// en una sola pasada (un valor que contenga «{{Monto}}» no se vuelve a reemplazar), en una sola línea y con los
/// signos «&lt;» «&gt;» escapados, para que ningún dato pueda cerrar el bloque &lt;transaccion&gt;.
/// </summary>
public static class DiagnosticPromptBuilder
{
    public const string Version = "v2";
    public const string ResourceName = "SmartOps.Web.Prompts.diagnostic.v2.md";

    private const string SystemMarker = "<!-- @system -->";
    private const string UserMarker = "<!-- @user -->";

    private static readonly Regex Placeholder = new(@"\{\{(?<name>[A-Za-z]+)\}\}", RegexOptions.Compiled);
    private static readonly Lazy<(string System, string User)> Template = new(LoadTemplate);

    public static DiagnosticPrompt Build(Transaction tx)
    {
        ArgumentNullException.ThrowIfNull(tx);

        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Id"] = tx.Id.ToString(CultureInfo.InvariantCulture),
            ["CustomerId"] = tx.CustomerId.ToString(CultureInfo.InvariantCulture),
            ["Monto"] = DiagnosticFormatting.FormatAmount(tx.Amount, tx.Currency),
            ["Moneda"] = tx.Currency,
            ["Tarjeta"] = DiagnosticFormatting.FormatCard(tx.CardLast4),
            ["Procesador"] = tx.Provider,
            ["Referencia"] = tx.GatewayReference,
            ["Estado"] = tx.Status,
            ["Motivo"] = tx.ErrorMessage,
            ["Fecha"] = tx.OccurredAt?.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
        };

        var (system, userTemplate) = Template.Value;
        var user = Placeholder.Replace(userTemplate, m =>
        {
            var name = m.Groups["name"].Value;
            if (!values.TryGetValue(name, out var value))
            {
                throw new InvalidOperationException($"Placeholder desconocido en {ResourceName}: {{{{{name}}}}}");
            }

            return EscapeValue(value);
        });

        return new DiagnosticPrompt(Version, system, user, tx);
    }

    /// <summary>Un dato es una sola línea y nunca abre ni cierra etiquetas.</summary>
    internal static string EscapeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "(sin datos)";

        var single = Regex.Replace(value, @"\s*[\r\n]+\s*", " ").Trim();
        return single.Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static (string System, string User) LoadTemplate()
    {
        using var stream = typeof(DiagnosticPromptBuilder).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"No se encontró el recurso embebido {ResourceName}.");
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd().Replace("\r\n", "\n");

        var s = text.IndexOf(SystemMarker, StringComparison.Ordinal);
        var u = text.IndexOf(UserMarker, StringComparison.Ordinal);
        if (s < 0 || u < s)
        {
            throw new InvalidOperationException($"{ResourceName} debe tener los marcadores {SystemMarker} y {UserMarker}, en ese orden.");
        }

        var system = text.Substring(s + SystemMarker.Length, u - s - SystemMarker.Length).Trim();
        var user = text.Substring(u + UserMarker.Length).Trim();
        return (system, user);
    }
}
