using System.Net;

namespace SQLBI.Whiteboard.Core.Import;

/// <summary>
/// The start tags of an HTML page, in document order, with their attributes and the
/// text that follows each one up to the next tag. Text that follows an end tag, such as
/// a title after a bold ID, comes as a tag named <c>#text</c>. The Microsoft Whiteboard
/// export is generated markup, so quoting is regular, but it is HTML rather than XML:
/// some elements are never closed.
/// </summary>
internal static class HtmlTags
{
    /// <summary>
    /// Text longer than this is not kept. The only text on the page is what people
    /// typed, and the long runs are stylesheets and scripts.
    /// </summary>
    private const int MaximumText = 65536;

    public const string TextName = "#text";

    private static readonly Dictionary<string, string> NoAttributes = new(StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<HtmlTag> StartTags(string html, int start)
    {
        var index = html.LastIndexOf('<', Math.Clamp(start, 0, Math.Max(0, html.Length - 1)));
        index = Math.Max(0, index);
        while (index < html.Length)
        {
            var open = html.IndexOf('<', index);
            if (open < 0 || open + 1 >= html.Length)
            {
                yield break;
            }

            if (html[open + 1] == '/')
            {
                var close = html.IndexOf('>', open);
                if (close < 0)
                {
                    yield break;
                }

                var after = html.IndexOf('<', close);
                var length = (after < 0 ? html.Length : after) - close - 1;
                if (length is > 0 and < MaximumText && !html.AsSpan(close + 1, length).IsWhiteSpace())
                {
                    yield return new HtmlTag(TextName, NoAttributes, WebUtility.HtmlDecode(html.Substring(close + 1, length)));
                }

                index = close + 1;
                continue;
            }

            if (!char.IsAsciiLetter(html[open + 1]))
            {
                index = open + 1;
                continue;
            }

            var cursor = open + 1;
            while (cursor < html.Length && (char.IsAsciiLetterOrDigit(html[cursor]) || html[cursor] == '-'))
            {
                cursor++;
            }

            var name = html[(open + 1)..cursor].ToLowerInvariant();
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (cursor < html.Length && html[cursor] != '>')
            {
                if (char.IsWhiteSpace(html[cursor]) || html[cursor] == '/')
                {
                    cursor++;
                    continue;
                }

                var nameStart = cursor;
                while (cursor < html.Length && !char.IsWhiteSpace(html[cursor]) && html[cursor] is not ('=' or '>' or '/'))
                {
                    cursor++;
                }

                var attributeName = html[nameStart..cursor];
                while (cursor < html.Length && char.IsWhiteSpace(html[cursor]))
                {
                    cursor++;
                }

                if (cursor >= html.Length || html[cursor] != '=')
                {
                    attributes.TryAdd(attributeName, string.Empty);
                    continue;
                }

                cursor++;
                while (cursor < html.Length && char.IsWhiteSpace(html[cursor]))
                {
                    cursor++;
                }

                string value;
                if (cursor < html.Length && html[cursor] is '"' or '\'')
                {
                    var quote = html[cursor];
                    var end = html.IndexOf(quote, cursor + 1);
                    if (end < 0)
                    {
                        yield break;
                    }

                    value = html[(cursor + 1)..end];
                    cursor = end + 1;
                }
                else
                {
                    var valueStart = cursor;
                    while (cursor < html.Length && !char.IsWhiteSpace(html[cursor]) && html[cursor] != '>')
                    {
                        cursor++;
                    }

                    value = html[valueStart..cursor];
                }

                // Picture data is base64 and never holds an entity, and decoding
                // megabytes of it would only copy them.
                attributes.TryAdd(attributeName, value.Contains('&') && value.Length < MaximumText ? WebUtility.HtmlDecode(value) : value);
            }

            var textStart = Math.Min(cursor + 1, html.Length);
            var textEnd = html.IndexOf('<', textStart);
            if (textEnd < 0)
            {
                textEnd = html.Length;
            }

            var text = textEnd - textStart is > 0 and < MaximumText
                ? WebUtility.HtmlDecode(html[textStart..textEnd])
                : string.Empty;
            yield return new HtmlTag(name, attributes, text);
            index = textStart;
        }
    }
}

internal sealed record HtmlTag(string Name, IReadOnlyDictionary<string, string> Attributes, string Text = "")
{
    public string? Attribute(string name) => Attributes.TryGetValue(name, out var value) ? value : null;

    public bool HasClass(string name) =>
        Attribute("class") is { } classes &&
        classes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(name, StringComparer.Ordinal);
}
