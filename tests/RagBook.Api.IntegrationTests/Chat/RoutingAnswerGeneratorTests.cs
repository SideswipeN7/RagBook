using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.Options;
using RagBook.Infrastructure.SharedContext.Providers.Cli;
using RagBook.Modules.Chat;
using RagBook.Modules.Chat.Domain;
using RagBook.Modules.Settings.Domain;
using RagBook.Modules.Settings.Errors;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Api.IntegrationTests.Chat;

/// <summary>
/// Unit tests for <see cref="RoutingAnswerGenerator"/> (US-22): a usable key routes to the Anthropic generator
/// (unchanged); no key + CLI enabled routes to the CLI generator; no key + CLI disabled throws the existing
/// key-missing failure (InvalidKey for a session ask, Unavailable for a demo ask). Fakes stand in for both
/// generators and the client factory, so nothing hits a real provider or process.
/// </summary>
public sealed class RoutingAnswerGeneratorTests
{
    private static RoutingAnswerGenerator CreateSut(
        RecordingGenerator anthropic,
        RecordingGenerator cli,
        IAnthropicClientFactory factory,
        bool cliEnabled)
    {
        return new RoutingAnswerGenerator(
            anthropic, cli, factory, Options.Create(new ClaudeCliOptions { Enabled = cliEnabled }));
    }

    [Fact]
    public async Task Should_RouteToAnthropic_When_SessionKeyPresent()
    {
        var anthropic = RecordingGenerator.Yielding("anthropic");
        var cli = RecordingGenerator.Yielding("cli");
        var sut = CreateSut(anthropic, cli, new FakeClientFactory(sessionOk: true, demoOk: false), cliEnabled: true);

        List<string> deltas = await Drain(sut, demo: false);

        deltas.Should().Equal("anthropic");
        anthropic.Called.Should().BeTrue();
        cli.Called.Should().BeFalse();
    }

    [Fact]
    public async Task Should_RouteToAnthropic_ForDemo_When_ApplicationKeyPresent()
    {
        var anthropic = RecordingGenerator.Yielding("anthropic");
        var cli = RecordingGenerator.Yielding("cli");
        var sut = CreateSut(anthropic, cli, new FakeClientFactory(sessionOk: false, demoOk: true), cliEnabled: true);

        List<string> deltas = await Drain(sut, demo: true);

        deltas.Should().Equal("anthropic");
        cli.Called.Should().BeFalse();
    }

    [Fact]
    public async Task Should_RouteToCli_When_NoKey_And_CliEnabled()
    {
        var anthropic = RecordingGenerator.Yielding("anthropic");
        var cli = RecordingGenerator.Yielding("cli");
        var sut = CreateSut(anthropic, cli, new FakeClientFactory(sessionOk: false, demoOk: false), cliEnabled: true);

        List<string> deltas = await Drain(sut, demo: false);

        deltas.Should().Equal("cli");
        anthropic.Called.Should().BeFalse();
        cli.Called.Should().BeTrue();
    }

    [Fact]
    public async Task Should_ThrowInvalidKey_When_NoSessionKey_And_CliDisabled()
    {
        var sut = CreateSut(
            RecordingGenerator.Yielding("anthropic"),
            RecordingGenerator.Yielding("cli"),
            new FakeClientFactory(sessionOk: false, demoOk: false),
            cliEnabled: false);

        (await Act(sut, demo: false).Should().ThrowAsync<AnswerGenerationException>())
            .Which.Failure.Should().Be(AnswerGenerationFailure.InvalidKey);
    }

    [Fact]
    public async Task Should_ThrowUnavailable_ForDemo_When_NoApplicationKey_And_CliDisabled()
    {
        var sut = CreateSut(
            RecordingGenerator.Yielding("anthropic"),
            RecordingGenerator.Yielding("cli"),
            new FakeClientFactory(sessionOk: false, demoOk: false),
            cliEnabled: false);

        (await Act(sut, demo: true).Should().ThrowAsync<AnswerGenerationException>())
            .Which.Failure.Should().Be(AnswerGenerationFailure.Unavailable);
    }

    private static Func<Task> Act(IAnswerGenerator sut, bool demo)
    {
        return () => Drain(sut, demo);
    }

    private static async Task<List<string>> Drain(IAnswerGenerator sut, bool demo)
    {
        var context = new GroundedContext([], "system", "user", IsDemo: demo);
        var deltas = new List<string>();
        await foreach (string delta in sut.GenerateAsync(context, CancellationToken.None))
        {
            deltas.Add(delta);
        }

        return deltas;
    }

    private sealed class RecordingGenerator(string marker) : IAnswerGenerator
    {
        public bool Called { get; private set; }

        public static RecordingGenerator Yielding(string marker)
        {
            return new RecordingGenerator(marker);
        }

        public async IAsyncEnumerable<string> GenerateAsync(
            GroundedContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Called = true;
            await Task.CompletedTask;
            yield return marker;
        }
    }

    private sealed class FakeClientFactory(bool sessionOk, bool demoOk) : IAnthropicClientFactory
    {
        public Result<AnthropicClientHandle> CreateForSession()
        {
            return sessionOk
                ? new AnthropicClientHandle("sk-session")
                : Result.Failure<AnthropicClientHandle>(SettingsErrors.ApiKeyMissing);
        }

        public Result<AnthropicClientHandle> CreateForDemo()
        {
            return demoOk
                ? new AnthropicClientHandle("sk-app")
                : Result.Failure<AnthropicClientHandle>(SettingsErrors.ApiKeyMissing);
        }
    }
}
