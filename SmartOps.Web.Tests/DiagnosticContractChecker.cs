using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SmartOps.Core.Entities;
using SmartOps.Web.Services;

namespace SmartOps.Web.Tests;

/// <summary>
/// Verifica el contrato de salida del diagnóstico v2 (Prompts/diagnostic.v2.md). Devuelve las violaciones encontradas;
/// lista vacía = cumple. Lo usan los tests del Simulador y la verificación manual contra un LLM real.
/// </summary>
public static class DiagnosticContractChecker
{
    public static readonly string[] Headings = { "## Qué pasó", "## Qué hacer", "## Mensaje para el cliente" };

    private static readonly Regex SentenceEnd = new(@"[.!?…](?=\s|$)", RegexOptions.Compiled);
    private static readonly Regex NumberedItem = new(@"^(?<n>\d+)\.\s+\S", RegexOptions.Compiled);
    private static readonly Regex Ustedeo = new(@"\b(usted|ustedes|estimad[oa]s?|su (tarjeta|pago|banco|compra|cuenta)|le (informamos|pedimos|recomendamos|sugerimos)|puede usted|intente|verifique|comuníquese|contacte)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Voseo = new(@"\b(tu|tus|te|vos|podés|revisá|intentá|comunicate|contactá|elegí|volvé|querés|tenés|usá|actualizá|consultá|probá|reconocés)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BadVoseo = new(@"\b(comunicá con|comunicáte|puedes|prefieres|desees|quieres|tienes|intenta|revisa|vuelve|usa)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SecurityWords = new(@"(fraud|sospech|robad|\brobo\b|perdid|bloque)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Promise = new(@"(reintegr|reembols|devoluci|devolver|te devolvemos|\b\d+\s*(horas|hs|días|dias|minutos)\b|garantiz|excepci)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<string> Check(string? markdown, Transaction tx)
    {
        var v = new List<string>();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            v.Add("respuesta vacía");
            return v;
        }

        var text = markdown.Replace("\r\n", "\n").Trim();
        if (text.Contains("```")) v.Add("contiene un bloque de código");
        if (text.EnumerateRunes().Any(r => Rune.GetUnicodeCategory(r) == UnicodeCategory.OtherSymbol)) v.Add("contiene emojis o símbolos");

        var lines = text.Split('\n');
        var headingLines = lines.Select((l, i) => (l: l.TrimEnd(), i)).Where(x => x.l.TrimStart().StartsWith('#')).ToList();
        var headingTexts = headingLines.Select(x => x.l.Trim()).ToArray();
        if (!headingTexts.SequenceEqual(Headings))
        {
            v.Add($"títulos distintos al contrato: [{string.Join(" | ", headingTexts)}]");
            return v;
        }

        if (headingLines[0].i != 0) v.Add("hay texto antes del primer título");

        string Section(int k)
        {
            var start = headingLines[k].i + 1;
            var end = k + 1 < headingLines.Count ? headingLines[k + 1].i : lines.Length;
            return string.Join("\n", lines[start..end]).Trim();
        }

        // Qué pasó: 1 a 3 oraciones.
        var what = Section(0);
        var whatSentences = CountSentences(what);
        if (whatSentences is < 1 or > 3) v.Add($"«Qué pasó» tiene {whatSentences} oraciones (1 a 3)");

        if (!string.IsNullOrWhiteSpace(tx.ErrorMessage) && !what.Contains(tx.ErrorMessage.Trim(), System.StringComparison.OrdinalIgnoreCase))
            v.Add($"«Qué pasó» no cita el motivo del procesador «{tx.ErrorMessage}»");

        // Qué hacer: lista numerada de 3 a 5 pasos, sin otro texto.
        var todoLines = Section(1).Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        var numbers = todoLines.Select(l => NumberedItem.Match(l.Trim())).ToList();
        if (numbers.Any(m => !m.Success)) v.Add("«Qué hacer» tiene líneas que no son pasos numerados");
        var steps = numbers.Where(m => m.Success).Select(m => int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture)).ToList();
        if (steps.Count is < 3 or > 5) v.Add($"«Qué hacer» tiene {steps.Count} pasos (3 a 5)");
        if (!steps.SequenceEqual(Enumerable.Range(1, steps.Count))) v.Add("«Qué hacer» no está numerado 1, 2, 3…");

        // Mensaje para el cliente.
        var msg = Section(2);
        var msgSentences = CountSentences(msg);
        if (msgSentences is < 1 or > 3) v.Add($"el mensaje tiene {msgSentences} oraciones (máximo 3)");
        var amount = DiagnosticFormatting.FormatAmount(tx.Amount, tx.Currency);
        if (!msg.Contains(amount)) v.Add($"el mensaje no usa el monto «{amount}»");
        if (!string.IsNullOrWhiteSpace(tx.CardLast4) && !msg.Contains($"terminada en {tx.CardLast4}")) v.Add($"el mensaje no usa «terminada en {tx.CardLast4}»");
        if (msg.Contains('[') || msg.Contains(']')) v.Add("el mensaje tiene placeholders entre corchetes");
        if (!string.IsNullOrWhiteSpace(tx.ErrorMessage) && msg.Contains(tx.ErrorMessage, System.StringComparison.OrdinalIgnoreCase)) v.Add("el mensaje menciona el motivo del procesador");
        if (Ustedeo.IsMatch(msg)) v.Add($"el mensaje usa «usted»: «{Ustedeo.Match(msg).Value}»");
        if (!Voseo.IsMatch(msg)) v.Add("el mensaje no tiene marcas de voseo / tuteo");
        if (BadVoseo.IsMatch(msg)) v.Add($"voseo incorrecto o tuteo: «{BadVoseo.Match(msg).Value}»");
        if (SecurityWords.IsMatch(msg)) v.Add($"el mensaje menciona un motivo de seguridad: «{SecurityWords.Match(msg).Value}»");
        if (Promise.IsMatch(msg)) v.Add($"el mensaje promete algo: «{Promise.Match(msg).Value}»");

        return v;
    }

    private static int CountSentences(string text)
    {
        var plain = Regex.Replace(text, @"\s+", " ").Trim();
        if (plain.Length == 0) return 0;
        var n = SentenceEnd.Matches(plain).Count;
        return SentenceEnd.IsMatch(plain[^1].ToString()) ? n : n + 1;
    }
}
