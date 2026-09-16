namespace SQLBI.Whiteboard.Core.Model;

// A view of the source, not a rewrite: the board and clipboard always keep the original text.
public readonly record struct PromptLine(string Text, int MarkerOffset, int ContentOffset)
{
    public bool IsBullet => MarkerOffset >= 0;

    public string Content => Text[ContentOffset..];
}

public static class PromptText
{
    public static IEnumerable<PromptLine> Lines(string source) =>
        source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').Select(ParseLine);

    public static PromptLine ParseLine(string text)
    {
        var marker = 0;
        while (marker < text.Length && text[marker] == ' ')
        {
            marker++;
        }

        if (marker + 1 >= text.Length || text[marker] != '-' || text[marker + 1] != ' ')
        {
            return new PromptLine(text, -1, 0);
        }

        var content = marker + 2;
        while (content < text.Length && text[content] == ' ')
        {
            content++;
        }

        return new PromptLine(text, marker, content);
    }
}
