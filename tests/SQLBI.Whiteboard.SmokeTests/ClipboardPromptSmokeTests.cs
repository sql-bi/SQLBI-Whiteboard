using System.IO;
using System.Text;
using System.Windows;

namespace SQLBI.Whiteboard.SmokeTests;

internal static class ClipboardPromptSmokeTests
{
    private const string Metadata = "{\"type\":\"Prompt\",\"version\":1}";
    private const string Source = "  Explain this query.\r\n- Keep Unicode café 世界 and the original whitespace.\r\n";

    public static void Run()
    {
        // Exercise real WPF data objects without replacing the user's system clipboard.
        foreach (var payload in new object[]
        {
            Metadata,
            Encoding.UTF8.GetBytes(Metadata),
            Encoding.UTF8.GetBytes("\uFEFF" + Metadata + "\0\0"),
            Encoding.UTF8.GetBytes("{ \"version\": 1, \"type\": \"Prompt\", \"extra\": true }"),
        })
        {
            var data = Tagged(payload);
            Assert(ClipboardPrompt.TryGetText(data, out var text) && text == Source,
                "Supported metadata representations must select Prompt without changing the source.");
        }

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Metadata));
        stream.Position = stream.Length;
        Assert(ClipboardPrompt.TryGetText(Tagged(stream), out var streamed) && streamed == Source &&
            stream.Position == stream.Length && stream.CanRead,
            "A native-style UTF-8 stream must be read from the start, left open, and have its position restored.");

        var mixed = Tagged(Encoding.UTF8.GetBytes(Metadata));
        mixed.SetData("PNG", new byte[] { 1, 2, 3 }, autoConvert: false);
        mixed.SetData("image/svg+xml", "<svg xmlns=\"http://www.w3.org/2000/svg\" />", autoConvert: false);
        mixed.SetData(DataFormats.FileDrop, new[] { "preview.png" }, autoConvert: false);
        Assert(ClipboardPrompt.TryGetText(mixed, out var prompt) && prompt == Source,
            "An explicitly tagged prompt should remain text even when the clipboard also offers images or files.");

        foreach (var payload in new object[]
        {
            "not json", "null", "[]", "{}", "{\"type\":\"Prompt\"}",
            "{\"version\":1}", "{\"type\":\"Plain\",\"version\":1}",
            "{\"type\":\"Prompt\",\"version\":2}", "{\"type\":\"Prompt\",\"version\":\"1\"}",
            "{\"type\":false,\"version\":1}", "{\"type\":\"Prompt\",\"version\":1.5}",
            Encoding.Unicode.GetBytes(Metadata), new byte[] { 0xFF, 0xFE, 0xFF },
            new byte[5000], new object(),
        })
        {
            Assert(!ClipboardPrompt.TryGetText(Tagged(payload), out var text) && text.Length == 0,
                "Malformed, unsupported, or oversized metadata must fall back to ordinary paste.");
        }

        using var tooLong = new MemoryStream(new byte[5000]);
        Assert(!ClipboardPrompt.TryGetText(Tagged(tooLong), out _), "A stream payload must obey the same size bound.");
        using var unreadable = new MemoryStream();
        unreadable.Dispose();
        Assert(!ClipboardPrompt.TryGetText(Tagged(unreadable), out _), "Unreadable metadata must not block normal paste.");

        var ordinary = new DataObject();
        ordinary.SetText(Source, TextDataFormat.UnicodeText);
        ordinary.SetData("SQLBI.PromptAssistant.Metadata.v2", Metadata, autoConvert: false);
        Assert(!ClipboardPrompt.TryGetText(null, out _) && !ClipboardPrompt.TryGetText(ordinary, out _),
            "Text alone or a different metadata format must not select Prompt.");
        var noText = new DataObject();
        noText.SetData(ClipboardPrompt.MetadataFormat, Encoding.UTF8.GetBytes(Metadata), autoConvert: false);
        Assert(!ClipboardPrompt.TryGetText(noText, out _), "Metadata alone must not create a text container.");
        noText.SetText(string.Empty, TextDataFormat.UnicodeText);
        Assert(!ClipboardPrompt.TryGetText(noText, out _), "An empty clipboard body must not create a container.");
    }

    private static DataObject Tagged(object payload)
    {
        var data = new DataObject();
        data.SetText(Source, TextDataFormat.UnicodeText);
        data.SetData(ClipboardPrompt.MetadataFormat, payload, autoConvert: false);
        return data;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
