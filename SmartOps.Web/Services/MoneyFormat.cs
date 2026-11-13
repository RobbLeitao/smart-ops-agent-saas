using System.Globalization;

namespace SmartOps.Web.Services;

/// <summary>Montos como en los mockups: «$ 67,00» / «€ 12,50» (coma decimal, símbolo separado).</summary>
public static class MoneyFormat
{
    private static readonly CultureInfo EsAr = CultureInfo.GetCultureInfo("es-AR");

    public static string Format(decimal amount, string? currency)
    {
        var symbol = string.Equals(currency, "EUR", StringComparison.OrdinalIgnoreCase) ? "€" : "$";
        return $"{symbol} {amount.ToString("#,##0.00", EsAr)}";
    }
}
