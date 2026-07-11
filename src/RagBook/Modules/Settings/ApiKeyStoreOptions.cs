namespace RagBook.Modules.Settings;

/// <summary>
/// Configuration for the session-scoped BYOK key store. Bound from the <c>ApiKeyStore</c> section so
/// the key lifetime and the abuse throttle are config-driven — no magic numbers (constitution §VII).
/// </summary>
public sealed class ApiKeyStoreOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "ApiKeyStore";

    /// <summary>How long a stored key lives (mirrors the session sliding window by default).</summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Maximum save/validate attempts per session within <see cref="ThrottleWindow"/>.</summary>
    public int ThrottleMaxAttempts { get; set; } = 5;

    /// <summary>The fixed window over which <see cref="ThrottleMaxAttempts"/> is counted.</summary>
    public TimeSpan ThrottleWindow { get; set; } = TimeSpan.FromMinutes(1);
}
