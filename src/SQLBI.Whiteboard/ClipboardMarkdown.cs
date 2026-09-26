using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using HtmlAgilityPack;
using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard;

internal static class ClipboardMarkdown
{
    public const string Format = "text/markdown";
    private const int MaximumBytes = 4 * 1024 * 1024;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    public static bool TryGetContainerText(IDataObject? data, IEnumerable<string>? order,
        out string text, out string languageId)
    {
        bool rich = TryGetText(data, out text, out bool explicitMarkdown);
        languageId = TextLanguageIds.Plain;
        if (explicitMarkdown)
        {
            languageId = TextLanguageIds.Markdown;
            return true;
        }
        string? plain = data?.GetData(DataFormats.UnicodeText, autoConvert: true) as string;
        if (plain is { Length: > 0 })
        {
            string plainLanguage = TextLanguageRegistry.ResolveFromOrder(plain, order);
            // Keep established code-snippet detection, and the preference that
            // explicitly asks to keep all pastes plain, so an HTML <pre> beside
            // SQL does not turn the SQL editor into a Markdown document.
            if (!rich || TextLanguageIds.NormalizeOrder(order)[0] == TextLanguageIds.Plain ||
                plainLanguage is not (TextLanguageIds.Plain or TextLanguageIds.Markdown))
            {
                text = plain;
                languageId = plainLanguage;
                return true;
            }
        }
        if (rich) languageId = TextLanguageRegistry.ResolveFromOrder(text, order);
        return rich;
    }

    public static bool TryGetText(IDataObject? data, out string text, out bool explicitMarkdown)
    {
        text = string.Empty;
        explicitMarkdown = false;
        if (data is null) return false;
        try
        {
            foreach (string format in new[] { Format, "text/x-markdown" })
            {
                if (data.GetDataPresent(format, autoConvert: false) &&
                    Read(data.GetData(format, autoConvert: false)) is { Length: > 0 } source)
                {
                    text = source;
                    explicitMarkdown = true;
                    return true;
                }
            }

            string? plain = data.GetData(DataFormats.UnicodeText, autoConvert: true) as string;
            bool hasMarkdown = plain is { Length: <= MaximumBytes } && MarkdownContent.LooksLike(plain);
            string converted = string.Empty;
            bool hasHtml = data.GetDataPresent(DataFormats.Html, autoConvert: false) &&
                Read(data.GetData(DataFormats.Html, autoConvert: false)) is { } html &&
                TryConvertHtml(html, out converted);

            // Prefer exact source when both representations retain the structure.
            // A plain-text table containing one code span is not the Markdown
            // source of the response, and in that case its HTML carries more.
            if (hasMarkdown && (!hasHtml || MarkdownContent.StructureScore(plain!) >= MarkdownContent.StructureScore(converted)))
            {
                text = plain!;
                return true;
            }
            text = converted;
            return hasHtml;
        }
        catch (Exception exception) when (exception is DecoderFallbackException or IOException or
            NotSupportedException or ObjectDisposedException or ExternalException or ArgumentException)
        {
            // Optional rich formats must never prevent the ordinary text paste.
            return false;
        }
    }

    public static bool TryConvertHtml(string clipboardHtml, out string markdown)
    {
        markdown = string.Empty;
        if (clipboardHtml.Length > MaximumBytes) return false;
        string html = Fragment(clipboardHtml);
        var document = new HtmlDocument { OptionMaxNestedChildNodes = 128 };
        try
        {
            document.LoadHtml(html);
            if (!document.DocumentNode.Descendants().Any(node =>
                node.Name is "table" or "ul" or "ol" or "blockquote" or "pre" or
                    "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or
                    "strong" or "b" or "em" or "i" or "code" or "a"))
                return false;
            markdown = ConvertNode(document.DocumentNode, 0).Trim();
            return markdown.Length > 0;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static string Fragment(string html)
    {
        // CF_HTML offsets count UTF-8 bytes, not UTF-16 characters.
        var start = Regex.Match(html, @"(?m)^StartFragment:\s*(\d+)", RegexOptions.CultureInvariant);
        var end = Regex.Match(html, @"(?m)^EndFragment:\s*(\d+)", RegexOptions.CultureInvariant);
        if (start.Success && end.Success &&
            int.TryParse(start.Groups[1].Value, out int first) && int.TryParse(end.Groups[1].Value, out int last))
        {
            byte[] bytes = Utf8.GetBytes(html);
            if (first >= 0 && last > first && last <= bytes.Length)
                return Utf8.GetString(bytes, first, last - first);
        }
        const string opening = "<!--StartFragment-->";
        const string closing = "<!--EndFragment-->";
        int from = html.IndexOf(opening, StringComparison.OrdinalIgnoreCase);
        int to = html.IndexOf(closing, StringComparison.OrdinalIgnoreCase);
        if (from >= 0 && to > from) return html[(from + opening.Length)..to];
        int markup = html.IndexOf('<');
        return markup >= 0 ? html[markup..] : html;
    }

    private static string ConvertNode(HtmlNode node, int depth)
    {
        if (depth > 64 || node.NodeType == HtmlNodeType.Comment) return string.Empty;
        if (node.NodeType == HtmlNodeType.Text)
            return Escape(Regex.Replace(Decode(node.InnerText), @"\s+", " "));
        if (node.Name is "script" or "style" or "iframe" or "object" or "svg" or "button" or "head" ||
            node.GetAttributeValue("aria-hidden", "") == "true" || node.Attributes["hidden"] is not null)
            return string.Empty;
        if (node.Name == "pre")
        {
            var code = node.SelectSingleNode("./code") ?? node;
            // Generated Markdown uses LF. Normalize code after fragment extraction,
            // since CF_HTML offsets refer to the original UTF-8 bytes.
            string source = Decode(code.InnerText).ReplaceLineEndings("\n").TrimEnd('\n');
            string fence = new('`', Math.Max(3, LongestBackticks(source) + 1));
            string language = Regex.Match(code.GetAttributeValue("class", ""), @"(?:^|\s)language-([\w#+-]+)").Groups[1].Value;
            return $"\n\n{fence}{language}\n{source}\n{fence}\n\n";
        }
        if (node.Name == "table") return Table(node, depth);
        if (node.Name is "ul" or "ol") return List(node, depth);
        string body = string.Concat(node.ChildNodes.Select(child => ConvertNode(child, depth + 1)));
        return node.Name switch
        {
            "h1" or "h2" or "h3" or "h4" or "h5" or "h6" =>
                $"\n\n{new string('#', node.Name[1] - '0')} {body.Trim()}\n\n",
            "p" or "div" or "section" or "article" => $"\n\n{body.Trim()}\n\n",
            "br" => "  \n",
            "hr" => "\n\n---\n\n",
            "strong" or "b" => Wrap(body, "**"),
            "em" or "i" => Wrap(body, "*"),
            "s" or "del" => Wrap(body, "~~"),
            "code" => InlineCode(Decode(node.InnerText)),
            "blockquote" => "\n\n" + string.Join("\n", body.Trim().Split('\n').Select(line => "> " + line)) + "\n\n",
            "a" => Link(body, node.GetAttributeValue("href", "")),
            "img" => Escape("[Image: " + Decode(node.GetAttributeValue("alt", "image")) + "]"),
            _ => body,
        };
    }

    private static string List(HtmlNode node, int depth)
    {
        int number = int.TryParse(node.GetAttributeValue("start", "1"), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int start) ? start : 1;
        var result = new StringBuilder("\n\n");
        foreach (var item in node.ChildNodes.Where(child => child.Name == "li"))
        {
            string marker = node.Name == "ol" ? $"{number++}. " : "- ";
            string body = string.Concat(item.ChildNodes.Select(child => ConvertNode(child, depth + 1))).Trim();
            string indent = new(' ', marker.Length);
            result.Append(marker).Append(body.Replace("\n", "\n" + indent, StringComparison.Ordinal)).Append('\n');
        }
        return result.Append('\n').ToString();
    }

    private static string Table(HtmlNode table, int depth)
    {
        var rows = table.Descendants("tr").Where(row => row.Ancestors("table").FirstOrDefault() == table).ToArray();
        var cells = rows.Select(row => row.ChildNodes.Where(cell => cell.Name is "th" or "td").ToArray()).ToArray();
        int columns = cells.Select(row => row.Length).DefaultIfEmpty(0).Max();
        if (columns == 0) return string.Empty;
        var result = new StringBuilder("\n\n");
        for (int row = 0; row < cells.Length; row++)
        {
            result.Append("| ");
            for (int column = 0; column < columns; column++)
            {
                string value = column < cells[row].Length
                    ? string.Concat(cells[row][column].ChildNodes.Select(child => ConvertNode(child, depth + 1))).Trim()
                    : "";
                value = Regex.Replace(value, @"\s*\n\s*", "<br>");
                value = Regex.Replace(value, @"(?<!\\)\|", @"\|");
                result.Append(value).Append(" | ");
            }
            result.Append('\n');
            if (row == 0)
            {
                result.Append('|');
                foreach (var cell in Enumerable.Range(0, columns))
                {
                    string align = cell < cells[0].Length ? cells[0][cell].GetAttributeValue("align", "") : "";
                    string style = cell < cells[0].Length ? cells[0][cell].GetAttributeValue("style", "") : "";
                    if (style.Contains("text-align", StringComparison.OrdinalIgnoreCase))
                    {
                        if (style.Contains("right", StringComparison.OrdinalIgnoreCase)) align = "right";
                        else if (style.Contains("center", StringComparison.OrdinalIgnoreCase)) align = "center";
                    }
                    result.Append(align.ToLowerInvariant() switch { "right" => " ---: |", "center" => " :---: |", _ => " --- |" });
                }
                result.Append('\n');
            }
        }
        return result.Append('\n').ToString();
    }

    private static string Wrap(string value, string marker) => string.IsNullOrWhiteSpace(value) ? value :
        (char.IsWhiteSpace(value[0]) ? " " : "") + marker + value.Trim() + marker +
        (char.IsWhiteSpace(value[^1]) ? " " : "");

    private static string InlineCode(string source)
    {
        string fence = new('`', Math.Max(1, LongestBackticks(source) + 1));
        return fence + " " + source.ReplaceLineEndings(" ") + " " + fence;
    }

    private static int LongestBackticks(string source) =>
        Regex.Matches(source, "`+").Select(match => match.Length).DefaultIfEmpty(0).Max();

    private static string Link(string body, string target) =>
        Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "mailto"
            ? $"[{body.Trim()}](<{target.Replace(">", "%3E", StringComparison.Ordinal).Replace("<", "%3C", StringComparison.Ordinal)}>)"
            : body;

    private static string Decode(string text) => HtmlEntity.DeEntitize(text).Replace('\u00A0', ' ');
    private static string Escape(string text) => Regex.Replace(text, @"([\\`*_\[\]<>#|~+-])", @"\$1");

    private static string? Read(object? value)
    {
        if (value is string text) return text.Length <= MaximumBytes ? text.TrimEnd('\0') : null;
        if (value is byte[] bytes) return bytes.Length <= MaximumBytes ? Utf8.GetString(bytes).TrimEnd('\0') : null;
        if (value is not Stream stream || !stream.CanRead) return null;
        long? position = stream.CanSeek ? stream.Position : null;
        try
        {
            if (position.HasValue) stream.Position = 0;
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = stream.Read(buffer, 0, Math.Min(buffer.Length, MaximumBytes + 1 - (int)output.Length))) > 0)
            {
                output.Write(buffer, 0, read);
                if (output.Length > MaximumBytes) return null;
            }
            return Utf8.GetString(output.ToArray()).TrimEnd('\0');
        }
        finally
        {
            if (position.HasValue) stream.Position = position.Value;
        }
    }
}
