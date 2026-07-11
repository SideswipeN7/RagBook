using FluentAssertions;
using RagBook.Api.IntegrationTests.Settings.Fakes;
using RagBook.Infrastructure.SharedContext.Providers.Anthropic;
using RagBook.Modules.Settings.Domain;
using RagBook.Modules.Settings.Errors;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Api.IntegrationTests.Settings;

/// <summary>
/// Unit tests for <see cref="AnthropicClientFactory"/> — the generation guard (US-02 AC-3, FR-009).
/// With no active key it fails <see cref="SettingsErrors.ApiKeyMissing"/>; with a key it yields a handle.
/// </summary>
public sealed class AnthropicClientFactoryTests
{
    [Fact]
    public void Should_ReturnApiKeyMissing_When_NoKey()
    {
        // Arrange
        var sut = new AnthropicClientFactory(new InMemoryApiKeyStore());

        // Act
        Result<AnthropicClientHandle> result = sut.CreateForSession();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SettingsErrors.ApiKeyMissing);
    }

    [Fact]
    public void Should_ReturnClientHandle_When_KeyPresent()
    {
        // Arrange
        var store = new InMemoryApiKeyStore();
        store.Set(SettingsTestClient.ValidKey);
        var sut = new AnthropicClientFactory(store);

        // Act
        Result<AnthropicClientHandle> result = sut.CreateForSession();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ApiKey.Should().Be(SettingsTestClient.ValidKey);
    }
}
