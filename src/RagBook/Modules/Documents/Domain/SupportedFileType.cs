namespace RagBook.Modules.Documents.Domain;

/// <summary>The file types US-04 accepts. Classification is by content (US-04 research D1), not extension.</summary>
public enum SupportedFileType
{
    /// <summary>A real PDF (identified by the <c>%PDF-</c> signature).</summary>
    Pdf = 0,

    /// <summary>Plain UTF-8 text.</summary>
    PlainText = 1,

    /// <summary>Markdown (UTF-8 text with a <c>.md</c>/<c>.markdown</c> extension).</summary>
    Markdown = 2,
}

/// <summary>Canonical content types and the human-readable allowed-formats list for error messages.</summary>
public static class SupportedFileTypes
{
    /// <summary>The formats named in the "unsupported file type" message.</summary>
    public const string AllowedList = "PDF, TXT, Markdown";

    /// <summary>The canonical content-type string stored on the document (never the client-declared value).</summary>
    public static string ContentType(this SupportedFileType type)
    {
        return type switch
        {
            SupportedFileType.Pdf => "application/pdf",
            SupportedFileType.Markdown => "text/markdown",
            SupportedFileType.PlainText => "text/plain",
            _ => "application/octet-stream",
        };
    }
}
