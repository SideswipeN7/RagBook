namespace RagBook.Infrastructure.SharedContext.Providers.Anthropic;

/// <summary>
/// Configuration for reaching the Anthropic API for key validation (US-02). The base URL and version
/// header are config-driven, never hard-coded (constitution §V).
/// </summary>
public sealed class AnthropicOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Anthropic";

    /// <summary>Provider base URL.</summary>
    public string BaseUrl { get; set; } = "https://api.anthropic.com";

    /// <summary>Value for the required <c>anthropic-version</c> header.</summary>
    public string AnthropicVersion { get; set; } = "2023-06-01";
}
