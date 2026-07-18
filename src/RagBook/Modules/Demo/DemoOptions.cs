namespace RagBook.Modules.Demo;

/// <summary>
/// Configuration for demo mode (US-03). Bound from the <c>Demo</c> section so every limit and the seed manifest are
/// config-driven — no magic numbers (constitution §VII). The application key that pays for demo answers lives in
/// <c>AnthropicOptions.ApplicationKey</c> (a secret), never here and never in the repo.
/// </summary>
public sealed class DemoOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Demo";

    /// <summary>Maximum demo questions a single session may ask over its lifetime (AC-2).</summary>
    public int MaxQuestionsPerSession { get; set; } = 10;

    /// <summary>Maximum demo requests a single IP may make per rolling hour (AC-3).</summary>
    public int MaxQuestionsPerIpPerHour { get; set; } = 20;

    /// <summary>How long the per-session demo counter is retained (approximates the session lifetime).</summary>
    public int SessionCounterTtlHours { get; set; } = 24;

    /// <summary>The seed manifest — the demo documents created once at startup (idempotent by <see cref="DemoDocumentManifest.Id"/>).</summary>
    public List<DemoDocumentManifest> Documents { get; set; } = [];
}

/// <summary>
/// One demo document to seed (US-03). Idempotency is keyed on <see cref="Id"/>; <see cref="Text"/> carries the
/// inline content to index (kept simple — a fixed, small corpus). A future tier may swap this for asset paths.
/// </summary>
public sealed class DemoDocumentManifest
{
    /// <summary>Fixed document id (idempotency key across restarts).</summary>
    public Guid Id { get; set; }

    /// <summary>Display file name shown in the demo tree section.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Canonical content type (e.g. <c>text/plain</c>).</summary>
    public string ContentType { get; set; } = "text/plain";

    /// <summary>The document's text content, indexed by the normal chunk+embed pipeline at seed time.</summary>
    public string Text { get; set; } = string.Empty;
}
