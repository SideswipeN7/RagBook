using FluentAssertions;
using Microsoft.Extensions.Options;
using RagBook.Infrastructure.SharedContext.Providers.Cli;
using RagBook.Modules.Chat;
using RagBook.Modules.Chat.Domain;
using Xunit;

namespace RagBook.Api.IntegrationTests.Chat;

/// <summary>
/// Unit tests for <see cref="ClaudeCliAnswerGenerator"/> (US-22) driven by a fake <see cref="ICliRunner"/> — no real
/// process. A successful run (exit 0) yields the trimmed stdout as a single delta and builds the expected argument
/// list (including <c>--model</c> only when configured); a non-zero exit, empty output, or a launch fault maps to
/// <see cref="AnswerGenerationFailure.Unavailable"/>.
/// </summary>
public sealed class ClaudeCliAnswerGeneratorTests
{
    private static GroundedContext Context()
    {
        return new GroundedContext([], "SYSTEM", "USER PROMPT");
    }

    private static ClaudeCliAnswerGenerator CreateSut(FakeCliRunner runner, ClaudeCliOptions options)
    {
        return new ClaudeCliAnswerGenerator(runner, Options.Create(options));
    }

    [Fact]
    public async Task Should_YieldTrimmedStdout_AsSingleDelta_When_ExitZero()
    {
        var runner = FakeCliRunner.Returning(new CliResult(0, "  The answer.\n", string.Empty));
        var sut = CreateSut(runner, new ClaudeCliOptions { Enabled = true });

        var deltas = new List<string>();
        await foreach (string delta in sut.GenerateAsync(Context(), CancellationToken.None))
        {
            deltas.Add(delta);
        }

        deltas.Should().Equal("The answer.");
    }

    [Fact]
    public async Task Should_PassPromptOnStdin_AndBuildBaseArgs_WithoutModel_When_ModelNotSet()
    {
        var runner = FakeCliRunner.Returning(new CliResult(0, "ok", string.Empty));
        var sut = CreateSut(runner, new ClaudeCliOptions { Enabled = true, Command = "claude" });

        await Drain(sut);

        runner.Command.Should().Be("claude");
        runner.StandardInput.Should().Be("USER PROMPT");
        runner.Arguments.Should().Equal("-p", "--output-format", "text", "--append-system-prompt", "SYSTEM");
        runner.Arguments.Should().NotContain("--model");
    }

    [Fact]
    public async Task Should_IncludeModelArg_When_ModelConfigured()
    {
        var runner = FakeCliRunner.Returning(new CliResult(0, "ok", string.Empty));
        var sut = CreateSut(runner, new ClaudeCliOptions { Enabled = true, Model = "claude-haiku-4-5-20251001" });

        await Drain(sut);

        runner.Arguments.Should().Equal(
            "-p", "--output-format", "text", "--model", "claude-haiku-4-5-20251001", "--append-system-prompt", "SYSTEM");
    }

    [Fact]
    public async Task Should_ThrowUnavailable_When_NonZeroExit()
    {
        var runner = FakeCliRunner.Returning(new CliResult(1, string.Empty, "boom"));
        var sut = CreateSut(runner, new ClaudeCliOptions { Enabled = true });

        (await Act(sut).Should().ThrowAsync<AnswerGenerationException>())
            .Which.Failure.Should().Be(AnswerGenerationFailure.Unavailable);
    }

    [Fact]
    public async Task Should_ThrowUnavailable_When_EmptyOutput()
    {
        var runner = FakeCliRunner.Returning(new CliResult(0, "   \n", string.Empty));
        var sut = CreateSut(runner, new ClaudeCliOptions { Enabled = true });

        (await Act(sut).Should().ThrowAsync<AnswerGenerationException>())
            .Which.Failure.Should().Be(AnswerGenerationFailure.Unavailable);
    }

    [Fact]
    public async Task Should_ThrowUnavailable_When_RunnerFaults()
    {
        var runner = FakeCliRunner.Throwing(new System.ComponentModel.Win32Exception("The system cannot find the file specified."));
        var sut = CreateSut(runner, new ClaudeCliOptions { Enabled = true });

        (await Act(sut).Should().ThrowAsync<AnswerGenerationException>())
            .Which.Failure.Should().Be(AnswerGenerationFailure.Unavailable);
    }

    private static Func<Task> Act(IAnswerGenerator sut)
    {
        return () => Drain(sut);
    }

    private static async Task Drain(IAnswerGenerator sut)
    {
        await foreach (string _ in sut.GenerateAsync(Context(), CancellationToken.None))
        {
        }
    }

    private sealed class FakeCliRunner : ICliRunner
    {
        private readonly CliResult? _result;
        private readonly Exception? _exception;

        private FakeCliRunner(CliResult? result, Exception? exception)
        {
            _result = result;
            _exception = exception;
        }

        public string? Command { get; private set; }

        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public string? StandardInput { get; private set; }

        public static FakeCliRunner Returning(CliResult result)
        {
            return new FakeCliRunner(result, null);
        }

        public static FakeCliRunner Throwing(Exception exception)
        {
            return new FakeCliRunner(null, exception);
        }

        public Task<CliResult> RunAsync(
            string command,
            IReadOnlyList<string> arguments,
            string standardInput,
            CancellationToken cancellationToken)
        {
            Command = command;
            Arguments = arguments;
            StandardInput = standardInput;

            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_result!.Value);
        }
    }
}
