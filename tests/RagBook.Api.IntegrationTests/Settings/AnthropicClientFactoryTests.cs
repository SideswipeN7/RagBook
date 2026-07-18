using FluentAssertions;
using Microsoft.Extensions.Options;
using RagBook.Api.IntegrationTests.Settings.Fakes;
using RagBook.Infrastructure.SharedContext.Providers.Anthropic;
using RagBook.Modules.Demo.Errors;
using RagBook.Modules.Settings.Domain;
using RagBook.Modules.Settings.Errors;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Api.IntegrationTests.Settings;

/// <summary>
/// Unit tests for <see cref="AnthropicClientFactory"/> — the session BYOK guard (US-02 AC-3, FR-009) and the demo
/// application-key path (US-03). With no active session key it fails <see cref="SettingsErrors.ApiKeyMissing"/>;
/// with no application key <see cref="AnthropicClientFactory.CreateForDemo"/> fails <see cref="DemoErrors.Unavailable"/>.
/// </summary>
public sealed class AnthropicClientFactoryTests
{
    private static AnthropicClientFactory CreateSut(InMemoryApiKeyStore store, string? applicationKey = null)
    {
        return new AnthropicClientFactory(store, Options.Create(new AnthropicOptions { ApplicationKey = applicationKey }));
    }

    [Fact]
    public void Should_ReturnApiKeyMissing_When_NoKey()
    {
        // Arrange
        var sut = CreateSut(new InMemoryApiKeyStore());

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
        var sut = CreateSut(store);

        // Act
        Result<AnthropicClientHandle> result = sut.CreateForSession();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ApiKey.Should().Be(SettingsTestClient.ValidKey);
    }

    [Fact]
    public void Should_ReturnDemoUnavailable_When_NoApplicationKey()
    {
        // Arrange — a session key is present but no application key is configured.
        var store = new InMemoryApiKeyStore();
        store.Set(SettingsTestClient.ValidKey);
        var sut = CreateSut(store, applicationKey: null);

        // Act
        Result<AnthropicClientHandle> result = sut.CreateForDemo();

        // Assert — demo does not fall back to the session key.
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DemoErrors.Unavailable);
    }

    [Fact]
    public void Should_ReturnApplicationKey_When_Configured()
    {
        // Arrange — no session key, but an application key is configured (keyless demo, US-03 AC-1).
        var sut = CreateSut(new InMemoryApiKeyStore(), applicationKey: "sk-ant-app-demo");

        // Act
        Result<AnthropicClientHandle> result = sut.CreateForDemo();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ApiKey.Should().Be("sk-ant-app-demo");
    }
}
