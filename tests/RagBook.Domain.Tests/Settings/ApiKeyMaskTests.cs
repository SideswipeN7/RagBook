using FluentAssertions;
using RagBook.Modules.Settings.Domain;
using Xunit;

namespace RagBook.Domain.Tests.Settings;

/// <summary>
/// Unit tests for <see cref="ApiKeyMask"/> (US-02 AC-2). The mask must reveal only the recognizable
/// prefix and the last four characters, and defensively hide anything too short to mask safely.
/// </summary>
public sealed class ApiKeyMaskTests
{
    [Fact]
    public void Should_Mask_KeepingPrefixAndLast4_When_NormalKey()
    {
        // Arrange
        const string fullKey = "sk-ant-api03-abcdefghijklmnopB7fA";

        // Act
        string mask = ApiKeyMask.Mask(fullKey);

        // Assert
        mask.Should().Be("sk-ant-api03-…B7fA");
        mask.Should().NotContain("abcdefghijklmnop");
    }

    [Fact]
    public void Should_UseGenericPrefix_When_UnrecognizedShape()
    {
        // Arrange
        const string fullKey = "sk-live-XXXXXXXX9Z7Q";

        // Act
        string mask = ApiKeyMask.Mask(fullKey);

        // Assert
        mask.Should().Be("sk-…9Z7Q");
    }

    [Fact]
    public void Should_MaskFully_When_KeyTooShort()
    {
        // Arrange
        const string fullKey = "abc";

        // Act
        string mask = ApiKeyMask.Mask(fullKey);

        // Assert
        mask.Should().Be("…");
    }
}
