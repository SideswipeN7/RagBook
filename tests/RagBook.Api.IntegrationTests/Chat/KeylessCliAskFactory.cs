using Microsoft.AspNetCore.Hosting;

namespace RagBook.Api.IntegrationTests.Chat;

/// <summary>
/// A <see cref="ChatAskApiFactory"/> host with keyless CLI mode on (<c>ClaudeCli:Enabled=true</c>) and no session
/// key stored — so US-22's guard relaxation can be verified end-to-end: an ask with no key must reach the pipeline
/// (not 401). The generator is still the scriptable fake (inherited swap), so no real CLI process runs.
/// </summary>
public sealed class KeylessCliAskFactory : ChatAskApiFactory
{
    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ClaudeCli:Enabled", "true");
        base.ConfigureWebHost(builder);
    }
}
