using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartOps.Web.Services
{
    /// <summary>
    /// Extracts the "suggested message for the customer" section (third axis of a diagnostic) from the diagnostic
    /// Markdown and converts it to plain text ready to paste into an email or chat.
    /// Tolerates the heading variants produced by the Local Simulator and by real LLMs (Groq / gpt-oss / OpenAI):
    /// <c>## 📨 Mensaje sugerido para el cliente</c>, <c>## Mensaje sugerido para enviar al cliente</c>,
    /// <c>### 3. Mensaje al cliente</c>, <c>**Mensaje sugerido:**</c>, ...
    /// </summary>
    public static class SuggestedMessageExtractor
    {
        private static readonly Regex AtxHeading = new(@"^\s{0,3}#{1,6}\s*(?<text>.*?)\s*#*\s*$", RegexOptions.Compiled);
        private static readonly Regex BoldHeading = new(@"^\s*(?:\d+[.)]\s*)?(?:\*\*|__)(?<text>[^*_]+?)(?:\*\*|__)\s*:?\s*$", RegexOptions.Compiled);
        private static readonly Regex HorizontalRule = new(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$", RegexOptions.Compiled);
        private static readonly Regex BlockquotePrefix = new(@"^\s*(>\s?)+", RegexOptions.Compiled);
        private static readonly Regex EmphasisMarkers = new(@"\*\*|__|`", RegexOptions.Compiled);

        /// <summary>
        /// Returns true and the plain-text message when the diagnostic has a recognizable, non-empty customer
        /// message section; false otherwise (old diagnostics, unexpected format, empty input).
        /// </summary>
        public static bool TryExtractPlainText(string? markdown, out string message)
        {
            message = string.Empty;
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return false;
            }

            var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            var start = Array.FindIndex(lines, IsCustomerMessageHeading);
            if (start < 0)
            {
                return false;
            }

            var body = new List<string>();
            for (var i = start + 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (AtxHeading.IsMatch(line) || HorizontalRule.IsMatch(line) || IsSectionBoldHeading(line))
                {
                    break;
                }

                body.Add(ToPlainLine(line));
            }

            var text = NormalizeBlankLines(body);
            if (string.IsNullOrWhiteSpace(text) || text.Trim() == "-")
            {
                return false;
            }

            message = text;
            return true;
        }

        internal static bool IsCustomerMessageHeading(string line)
        {
            var heading = GetHeadingText(line);
            if (heading is null)
            {
                return false;
            }

            var normalized = Normalize(heading);
            return normalized.Contains("mensaje")
                   && (normalized.Contains("sugerido") || normalized.Contains("cliente"));
        }

        // A bold-only line that looks like another diagnostic section ends the message (some LLMs use bold
        // pseudo-headings instead of '#'). Bold lines inside the message itself (e.g. "**Atentamente,**") are kept.
        private static bool IsSectionBoldHeading(string line)
        {
            var match = BoldHeading.Match(line);
            if (!match.Success) return false;
            var normalized = Normalize(match.Groups["text"].Value);
            return normalized.Contains("razon") || normalized.Contains("accion") || normalized.Contains("resumen")
                   || normalized.Contains("causa") || normalized.Contains("recomendacion");
        }

        internal static bool IsAtxHeading(string line) => AtxHeading.IsMatch(line);

        internal static bool IsHorizontalRule(string line) => HorizontalRule.IsMatch(line);

        internal static string? GetHeadingText(string line)
        {
            var atx = AtxHeading.Match(line);
            if (atx.Success) return atx.Groups["text"].Value;

            var bold = BoldHeading.Match(line);
            if (bold.Success) return bold.Groups["text"].Value;

            return null;
        }

        private static string ToPlainLine(string line)
        {
            var plain = BlockquotePrefix.Replace(line, string.Empty);
            plain = EmphasisMarkers.Replace(plain, string.Empty);
            // Trailing double space is a Markdown hard line break: drop it together with any other trailing space.
            return plain.TrimEnd();
        }

        private static string NormalizeBlankLines(List<string> lines)
        {
            var sb = new StringBuilder();
            var pendingBlank = false;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    pendingBlank = sb.Length > 0;
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append('\n');
                    if (pendingBlank) sb.Append('\n');
                }

                sb.Append(line);
                pendingBlank = false;
            }

            return sb.ToString();
        }

        // Lowercase, without accents, emojis or punctuation: "📨 Mensaje Sugerido" -> "mensaje sugerido".
        internal static string Normalize(string text)
        {
            var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var c in decomposed)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
            }

            return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
