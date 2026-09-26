using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace SQLBI.Whiteboard;

internal static class ClipboardPrompt
{
    public const string MetadataFormat = "SQLBI.PromptAssistant.Metadata.v1";
    private const int MaximumMetadataBytes = 4096;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    public static bool TryGetText(IDataObject? data, out string text)
    {
        text = string.Empty;
        try
        {
            if (data is null || !data.GetDataPresent(MetadataFormat, autoConvert: false) ||
                ReadMetadata(data.GetData(MetadataFormat, autoConvert: false)) is not { } metadata)
            {
                return false;
            }

            using var json = JsonDocument.Parse(metadata.TrimStart('\uFEFF').TrimEnd('\0'));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                type.GetString() != "Prompt" ||
                !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var number) || number != 1 ||
                data.GetData(DataFormats.UnicodeText, autoConvert: true) is not string { Length: > 0 } source)
            {
                return false;
            }

            text = source;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException or
            IOException or NotSupportedException or ObjectDisposedException or ExternalException)
        {
            // Metadata is an optional hint from another process, so failing to read it keeps the normal paste.
            return false;
        }
    }

    private static string? ReadMetadata(object? value) => value switch
    {
        string text when text.Length <= MaximumMetadataBytes => text,
        byte[] bytes when bytes.Length <= MaximumMetadataBytes => Utf8.GetString(bytes),
        Stream stream => ReadStream(stream),
        _ => null,
    };

    private static string? ReadStream(Stream stream)
    {
        if (!stream.CanRead)
        {
            return null;
        }

        // Native custom formats arrive as streams. Leave them open and restore seekable positions.
        long? position = stream.CanSeek ? stream.Position : null;
        try
        {
            if (position.HasValue)
            {
                stream.Position = 0;
            }

            var buffer = new byte[MaximumMetadataBytes + 1];
            var count = 0;
            while (count < buffer.Length)
            {
                var read = stream.Read(buffer, count, buffer.Length - count);
                if (read == 0)
                {
                    break;
                }

                count += read;
            }

            return count <= MaximumMetadataBytes ? Utf8.GetString(buffer, 0, count) : null;
        }
        finally
        {
            if (position.HasValue)
            {
                stream.Position = position.Value;
            }
        }
    }
}
