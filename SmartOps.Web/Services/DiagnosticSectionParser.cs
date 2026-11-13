using System.Text;
using System.Text.RegularExpressions;
using Markdig;

namespace SmartOps.Web.Services;

/// <summary>
/// Diagnóstico partido en las secciones del Drawer. Cada sección es Markdown (sin el título).
/// <see cref="Other"/> junta las secciones con títulos que no se reconocen, para no perder contenido.
/// </summary>
public sealed record DiagnosticSections(
    string? WhatHappened,
    string? WhatToDo,
    string? CustomerMessage,
    string? Other,
    string FullMarkdown)
{
    /// <summary>True si se reconoció al menos una de las tres secciones con contenido.</summary>
    public bool HasSections =>
        !string.IsNullOrWhiteSpace(WhatHappened) || !string.IsNullOrWhiteSpace(WhatToDo) || !string.IsNullOrWhiteSpace(CustomerMessage);
}

/// <summary>
/// Parte el Markdown de un diagnóstico en «Qué pasó», «Qué hacer» y «Mensaje para el cliente».
/// Tolera los títulos del Simulador (con emojis: «## 🔍 Resumen del Error») y las variantes de los LLM
/// («## Razón del fallo», «### 2. Acción recomendada», «**Mensaje sugerido:**»…). Reusa la detección de
/// títulos de <see cref="SuggestedMessageExtractor"/>, así el botón Copiar y el Drawer coinciden.
/// </summary>
public static class DiagnosticSectionParser
{
    private enum Kind { None, WhatHappened, WhatToDo, CustomerMessage, Other }

    private static readonly Regex BulletItem = new(@"^(?<indent>\s{0,3})[-*+]\s+(?<text>.*)$", RegexOptions.Compiled);

    public static DiagnosticSections Parse(string? markdown)
    {
        var full = markdown ?? string.Empty;
        if (string.IsNullOrWhiteSpace(full))
        {
            return new DiagnosticSections(null, null, null, null, full);
        }

        var buckets = new Dictionary<Kind, List<string>>
        {
            [Kind.None] = new(), [Kind.WhatHappened] = new(), [Kind.WhatToDo] = new(),
            [Kind.CustomerMessage] = new(), [Kind.Other] = new()
        };

        // Sin el bloque ```markdown que algunos LLM ponen alrededor de todo el diagnóstico.
        full = SuggestedMessageExtractor.UnwrapOuterFence(full);

        var current = Kind.None;
        var lines = full.Split('\n');

        foreach (var line in lines)
        {
            var heading = SuggestedMessageExtractor.GetHeadingText(line);
            if (heading != null)
            {
                var kind = Classify(line, heading);
                if (kind != Kind.None)
                {
                    current = kind;
                    continue;
                }

                // Un título «#» que no se reconoce abre una sección aparte (como en el extractor del botón
                // Copiar, termina la sección anterior). Una línea en negrita suelta es contenido.
                if (SuggestedMessageExtractor.IsAtxHeading(line))
                {
                    current = Kind.Other;
                    buckets[Kind.Other].Add(line);
                    continue;
                }
            }

            // Separadores «---» entre secciones: no aportan nada dentro de una sección.
            if (SuggestedMessageExtractor.IsHorizontalRule(line)) continue;

            buckets[current].Add(line);
        }

        var preamble = Clean(buckets[Kind.None]);
        var whatHappened = Clean(buckets[Kind.WhatHappened]);
        var whatToDo = Clean(buckets[Kind.WhatToDo]);
        var message = Clean(buckets[Kind.CustomerMessage]);
        var other = Clean(buckets[Kind.Other]);

        var anyRecognized = whatHappened != null || whatToDo != null || message != null;
        if (!anyRecognized)
        {
            return new DiagnosticSections(null, null, null, null, full);
        }

        // Texto suelto antes del primer título: es contexto de «Qué pasó».
        if (preamble != null)
        {
            whatHappened = whatHappened == null ? preamble : preamble + "\n\n" + whatHappened;
        }

        return new DiagnosticSections(
            whatHappened,
            whatToDo == null ? null : ToOrderedList(whatToDo),
            message,
            other,
            full);
    }

    private static Kind Classify(string line, string headingText)
    {
        if (SuggestedMessageExtractor.IsCustomerMessageHeading(line)) return Kind.CustomerMessage;

        var n = SuggestedMessageExtractor.Normalize(headingText);
        if (n.Length == 0) return Kind.None;

        if (n.Contains("accion") || n.Contains("recomend") || n.Contains("que hacer") || n.Contains("pasos")
            || n.Contains("solucion") || n.Contains("operador") || n.Contains("siguientes"))
        {
            return Kind.WhatToDo;
        }

        if (n.Contains("resumen") || n.Contains("que paso") || n.Contains("causa") || n.Contains("razon")
            || n.Contains("diagnostico") || n.Contains("analisis") || n.Contains("motivo") || n.Contains("problema")
            || n.Contains("error"))
        {
            return Kind.WhatHappened;
        }

        return Kind.None;
    }

    private static string? Clean(List<string> lines)
    {
        var text = string.Join("\n", lines).Trim('\n', ' ', '\t');
        return string.IsNullOrWhiteSpace(text) || text.Trim() == "-" ? null : text;
    }

    // «Qué hacer» se muestra como lista numerada: las viñetas de primer nivel pasan a «1. 2. 3.».
    private static string ToOrderedList(string markdown)
    {
        var sb = new StringBuilder();
        var n = 0;
        foreach (var line in markdown.Split('\n'))
        {
            var m = BulletItem.Match(line);
            if (m.Success && m.Groups["indent"].Length == 0)
            {
                sb.Append(++n).Append(". ").Append(m.Groups["text"].Value).Append('\n');
            }
            else
            {
                sb.Append(line).Append('\n');
            }
        }

        return sb.ToString().TrimEnd('\n');
    }
}

/// <summary>Markdown → HTML con Markdig. El HTML crudo que venga del LLM nunca se inyecta.</summary>
public static class DiagnosticMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .Build();

    public static string ToHtml(string? markdown) =>
        string.IsNullOrWhiteSpace(markdown) ? string.Empty : Markdown.ToHtml(markdown, Pipeline);
}
