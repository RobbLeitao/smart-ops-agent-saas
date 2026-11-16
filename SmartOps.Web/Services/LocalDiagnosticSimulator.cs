namespace SmartOps.Web.Services;

/// <summary>
/// Simulador local: diagnóstico heurístico por motivo del procesador, con el mismo contrato que el prompt v2
/// (tres títulos fijos sin emojis, pasos numerados, mensaje al cliente en voseo con el monto y la tarjeta reales).
/// Lee los datos de <see cref="DiagnosticPrompt.Transaction"/>, no el texto del prompt.
/// </summary>
public static class LocalDiagnosticSimulator
{
    public const string Version = "sim-v2";

    public static AiDiagnosticResult Build(DiagnosticPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var tx = prompt.Transaction;
        var amount = prompt.FormattedAmount;
        var card = prompt.CardText;
        var reason = string.IsNullOrWhiteSpace(tx.ErrorMessage) ? null : tx.ErrorMessage.Trim();
        var processor = string.IsNullOrWhiteSpace(tx.Provider) ? "el procesador" : tx.Provider.Trim();
        var reference = string.IsNullOrWhiteSpace(tx.GatewayReference) ? "de la transacción" : tx.GatewayReference.Trim();
        var lower = reason?.ToLowerInvariant() ?? string.Empty;

        string whatHappened;
        string[] steps;
        string message;

        if (lower.Contains("stolen") || lower.Contains("robad") || lower.Contains("lost card"))
        {
            whatHappened = $"El emisor rechazó el pago de {amount} con el motivo «{reason}»: la tarjeta {card} figura denunciada y está bloqueada. No es un error técnico y el cobro no debe reintentarse con esa tarjeta.";
            steps = new[]
            {
                "No reintentar el cobro con esta tarjeta.",
                $"Marcar la transacción {reference} para revisión del equipo de riesgo.",
                $"Revisar si hubo otros intentos recientes del cliente {tx.CustomerId} con tarjetas distintas.",
                "Contactar al cliente solo por un canal registrado, sin mencionarle el motivo del emisor.",
                "Ofrecer completar la compra con otro medio de pago una vez verificada su identidad."
            };
            message = $"Hola, tu pago de {amount} con la tarjeta {card} no se pudo procesar por una restricción del banco emisor. Te pedimos que te comuniques con tu banco y, si querés, completes la compra con otro medio de pago.";
        }
        else if (lower.Contains("fraud") || lower.Contains("sospecha"))
        {
            whatHappened = $"{Capitalize(processor)} frenó el pago de {amount} con el motivo «{reason}»: sus controles antifraude detectaron un patrón de riesgo. No implica que el cliente haya hecho algo mal, pero el cobro no se completó.";
            steps = new[]
            {
                $"Revisar en {processor} las señales de riesgo de la referencia {reference} (IP, país, dispositivo).",
                "No reintentar el cobro hasta validar la operación.",
                "Contactar al cliente por un canal registrado para confirmar que reconoce la compra.",
                "Si la confirma, indicarle que puede reintentar o usar otro medio de pago; si no, escalar al equipo de riesgo."
            };
            message = $"Hola, tu pago de {amount} con la tarjeta {card} no se pudo completar porque quedó retenido en una verificación de seguridad. Si reconocés la compra, podés volver a intentarlo o comunicarte con tu banco para confirmarla.";
        }
        else if (lower.Contains("expired") || lower.Contains("venc"))
        {
            whatHappened = $"El pago de {amount} se rechazó con el motivo «{reason}»: la tarjeta {card} está vencida. El cobro no puede completarse hasta que el cliente actualice los datos.";
            steps = new[]
            {
                "Confirmar la fecha de vencimiento registrada para la tarjeta.",
                "Pedirle al cliente que actualice los datos de la tarjeta o cargue una nueva.",
                "Reintentar el cobro una vez actualizados los datos.",
                "Revisar si el cliente tiene otros cobros programados con la misma tarjeta."
            };
            message = $"Hola, no pudimos procesar tu pago de {amount} porque la tarjeta {card} está vencida. Podés actualizar los datos de la tarjeta o elegir otro medio de pago para completarlo.";
        }
        else if (lower.Contains("insufficient") || lower.Contains("fondos"))
        {
            whatHappened = $"El banco emisor rechazó el pago de {amount} con el motivo «{reason}»: la tarjeta {card} no tenía saldo o límite disponible al momento del cobro.";
            steps = new[]
            {
                "Verificar que no haya cobros duplicados para el mismo cliente.",
                "No reintentar el cobro de inmediato para evitar rechazos repetidos.",
                "Sugerir al cliente reintentar más tarde o usar otro medio de pago.",
                "Registrar el contacto con el cliente en el caso."
            };
            message = $"Hola, tu pago de {amount} con la tarjeta {card} no se pudo procesar porque el banco informó saldo insuficiente. Podés volver a intentarlo más tarde o usar otro medio de pago.";
        }
        else if (lower.Contains("cvc") || lower.Contains("cvv") || lower.Contains("security code"))
        {
            whatHappened = $"El pago de {amount} se rechazó con el motivo «{reason}»: el código de seguridad ingresado no coincide con el de la tarjeta {card}.";
            steps = new[]
            {
                "Confirmar que el rechazo se debe solo al código de seguridad.",
                "Pedirle al cliente que revise el código y vuelva a intentar el pago.",
                "Si el rechazo se repite varias veces, revisar la operación con el equipo de riesgo."
            };
            message = $"Hola, tu pago de {amount} con la tarjeta {card} no se pudo procesar porque el código de seguridad no coincide. Revisá el código en el dorso de la tarjeta y volvé a intentarlo.";
        }
        else if (lower.Contains("declined") || lower.Contains("rechaz"))
        {
            whatHappened = $"El banco emisor rechazó el pago de {amount} con el motivo «{reason}», sin dar más detalle. Suele deberse a una restricción del banco, un límite alcanzado o un bloqueo preventivo.";
            steps = new[]
            {
                $"Revisar en {processor} el código de rechazo detallado de la referencia {reference}.",
                "Confirmar que no haya reintentos automáticos en curso para esta transacción.",
                "Sugerir al cliente que consulte con su banco o pruebe con otro medio de pago.",
                "Escalar el caso si el rechazo se repite con la misma tarjeta."
            };
            message = $"Hola, tu pago de {amount} con la tarjeta {card} fue rechazado por el banco emisor. Te recomendamos consultar con tu banco o intentar con otro medio de pago.";
        }
        else
        {
            var motive = reason is null ? "sin un motivo informado" : $"con el motivo «{reason}»";
            whatHappened = $"El pago de {amount} falló {motive}, que el Simulador local no tiene catalogado. Hace falta revisar el detalle en {processor}.";
            steps = new[]
            {
                $"Revisar en {processor} el detalle de la referencia {reference}.",
                "Confirmar el estado final de la transacción antes de contactar al cliente.",
                "Configurar un proveedor de IA en /integrations para obtener un diagnóstico completo."
            };
            message = $"Hola, tu pago de {amount} con la tarjeta {card} no se pudo procesar. Podés volver a intentarlo o usar otro medio de pago.";
        }

        var numbered = string.Join("\n", steps.Select((s, i) => $"{i + 1}. {s}"));
        var markdown = $"## Qué pasó\n{whatHappened}\n\n## Qué hacer\n{numbered}\n\n## Mensaje para el cliente\n{message}";
        return new AiDiagnosticResult(markdown, Version);
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
