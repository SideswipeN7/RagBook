using FluentAssertions;
using NSubstitute;
using RagBook.Modules.Settings.Domain;
using RagBook.Modules.Settings.Errors;
using RagBook.Modules.Settings.Features.SetApiKey;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Application.Tests.Settings;

/// <summary>
/// Unit tests for <see cref="SetApiKeyCommandHandler"/> (US-02 AC-1). Verifies the throttle→validate→store
/// order, the three-way validation outcome, and that a key is stored only when the provider accepts it.
/// </summary>
public sealed class SetApiKeyCommandHandlerTests
{
    private const string ValidKey = "sk-ant-api03-abcdefghijklmnopB7fA";

    private readonly IApiKeyThrottle _throttle = Substitute.For<IApiKeyThrottle>();
    private readonly IApiKeyValidator _validator = Substitute.For<IApiKeyValidator>();
    private readonly IApiKeyStore _store = Substitute.For<IApiKeyStore>();

    public SetApiKeyCommandHandlerTests()
    {
        _throttle.TryRegisterAttempt().Returns(true);
    }

    private SetApiKeyCommandHandler CreateSut()
    {
        return new SetApiKeyCommandHandler(_throttle, _validator, _store);
    }

    [Fact]
    public async Task Should_StoreAndReturnActive_When_ValidatorValid()
    {
        // Arrange
        _validator.ValidateAsync(ValidKey, Arg.Any<CancellationToken>()).Returns(ApiKeyValidationResult.Valid);
        var sut = CreateSut();

        // Act
        Result<ApiKeyStatusResponse> result = await sut.Handle(new SetApiKeyCommand(ValidKey), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("active");
        result.Value.MaskedKey.Should().Be("sk-ant-api03-…B7fA");
        _store.Received(1).Set(ValidKey);
    }

    [Fact]
    public async Task Should_ReturnInvalidApiKey_And_NotStore_When_ValidatorRejected()
    {
        // Arrange
        _validator.ValidateAsync(ValidKey, Arg.Any<CancellationToken>()).Returns(ApiKeyValidationResult.Rejected);
        var sut = CreateSut();

        // Act
        Result<ApiKeyStatusResponse> result = await sut.Handle(new SetApiKeyCommand(ValidKey), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SettingsErrors.InvalidApiKey);
        _store.DidNotReceive().Set(Arg.Any<string>());
    }

    [Fact]
    public async Task Should_ReturnValidationUnavailable_And_NotStore_When_ValidatorUnavailable()
    {
        // Arrange
        _validator.ValidateAsync(ValidKey, Arg.Any<CancellationToken>()).Returns(ApiKeyValidationResult.Unavailable);
        var sut = CreateSut();

        // Act
        Result<ApiKeyStatusResponse> result = await sut.Handle(new SetApiKeyCommand(ValidKey), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SettingsErrors.ValidationUnavailable);
        _store.DidNotReceive().Set(Arg.Any<string>());
    }

    [Fact]
    public async Task Should_ReturnTooManyAttempts_And_NotCallValidator_When_ThrottleExceeded()
    {
        // Arrange
        _throttle.TryRegisterAttempt().Returns(false);
        var sut = CreateSut();

        // Act
        Result<ApiKeyStatusResponse> result = await sut.Handle(new SetApiKeyCommand(ValidKey), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SettingsErrors.TooManyAttempts);
        await _validator.DidNotReceive().ValidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _store.DidNotReceive().Set(Arg.Any<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-anthropic-key")]
    [InlineData("sk-a")]
    public async Task Should_ReturnInvalidApiKey_Without_CallingValidator_When_KeyMalformed(string malformed)
    {
        // Arrange — an empty/malformed key is rejected locally with the same code as a provider rejection.
        var sut = CreateSut();

        // Act
        Result<ApiKeyStatusResponse> result = await sut.Handle(new SetApiKeyCommand(malformed), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SettingsErrors.InvalidApiKey);
        await _validator.DidNotReceive().ValidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _store.DidNotReceive().Set(Arg.Any<string>());
    }
}
