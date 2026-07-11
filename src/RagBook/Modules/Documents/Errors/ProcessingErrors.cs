namespace RagBook.Modules.Documents.Errors;

/// <summary>
/// Human-readable failure reasons recorded on a document when background processing (US-06) fails. These
/// are shown to the user (via the tree's failure tooltip), so they are plain-language, not error codes.
/// </summary>
public static class ProcessingErrors
{
    /// <summary>The file had no extractable text (encrypted/scan/corrupt/blank) — AC-2.</summary>
    public const string TextExtractionFailed = "PDF nie zawiera tekstu — skany nie są obsługiwane.";

    /// <summary>The embedding provider kept failing after the retry budget — AC-3.</summary>
    public const string EmbeddingProviderError = "Nie udało się wygenerować indeksu (błąd usługi embeddingów). Spróbuj ponownie później.";
}

/// <summary>
/// A transient embedding-provider failure (timeout/5xx). Thrown by the provider driver so the Wolverine
/// retry policy retries with backoff (US-06 AC-3); after the budget the handler records
/// <see cref="ProcessingErrors.EmbeddingProviderError"/>.
/// </summary>
public sealed class EmbeddingProviderException(string message, Exception? innerException = null)
    : Exception(message, innerException);
