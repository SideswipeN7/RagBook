using System.Text;
using RagBook.Modules.Documents.Errors;
using RagBook.Shared.Results;

namespace RagBook.Modules.Documents.Domain;

/// <summary>
/// Validates and classifies an upload **by its content**, not its extension or the client-declared
/// content type (US-04 AC-2, research D1). A PDF is identified by the <c>%PDF-</c> signature; any other
/// upload must be valid UTF-8 text (no NUL / disallowed control bytes) to be accepted, and is then
/// classified by extension. Anything else is rejected as an unsupported type.
/// </summary>
public static class FileTypeDetector
{
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    /// <summary>
    /// Detects the type of <paramref name="content"/> (using <paramref name="fileName"/> only to
    /// distinguish Markdown from plain text). Returns the type, or
    /// <see cref="DocumentErrors.UnsupportedFileType"/>.
    /// </summary>
    public static Result<SupportedFileType> Detect(ReadOnlySpan<byte> content, string fileName)
    {
        if (content.StartsWith(PdfSignature))
        {
            return SupportedFileType.Pdf;
        }

        if (!IsValidUtf8Text(content))
        {
            return DocumentErrors.UnsupportedFileType;
        }

        string extension = GetExtension(fileName);

        return extension is ".md" or ".markdown"
            ? SupportedFileType.Markdown
            : SupportedFileType.PlainText;
    }

    private static bool IsValidUtf8Text(ReadOnlySpan<byte> content)
    {
        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        string text;
        try
        {
            text = strictUtf8.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        foreach (char character in text)
        {
            // Reject binary/control content, but allow the usual text whitespace controls.
            if (char.IsControl(character) && character is not '\t' and not '\n' and not '\r')
            {
                return false;
            }
        }

        return true;
    }

    private static string GetExtension(string fileName)
    {
        int dot = fileName.LastIndexOf('.');

        return dot > 0 ? fileName[dot..].ToLowerInvariant() : string.Empty;
    }
}
