using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using RagBook.Modules.Chat;
using RagBook.Modules.Chat.Domain;

namespace RagBook.Infrastructure.SharedContext.Providers.Cli;

/// <summary>
/// Keyless <see cref="IAnswerGenerator"/> that generates the grounded answer through the local <c>claude</c> CLI
/// (US-22): <c>claude -p --output-format text [--model …] [--append-system-prompt …]</c> with the grounded user
/// message written to stdin. The prompt is passed as an explicit argument list + stdin (never a shell string), so
/// document/question content can't inject commands. The CLI carries its own auth, so no key is needed. Text mode
/// isn't token-streaming, so the whole answer is yielded as a single delta (the SSE plumbing is unchanged). Any
/// non-zero exit, empty output, timeout, or launch failure becomes <see cref="AnswerGenerationFailure.Unavailable"/>.
/// </summary>
public sealed class ClaudeCliAnswerGenerator(ICliRunner runner, IOptions<ClaudeCliOptions> options)
    : IAnswerGenerator
{
    private readonly ClaudeCliOptions options = options.Value;

    /// <inheritdoc />
    public async IAsyncEnumerable<string> GenerateAsync(
        GroundedContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string[] arguments = BuildArguments(context.SystemPrompt);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        CliResult result;
        try
        {
            result = await runner.RunAsync(options.Command, arguments, context.UserPrompt, timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // The caller (client disconnect) cancelled — propagate, don't mask as a provider failure.
        }
        catch (Exception)
        {
            // Launch failure (binary missing), timeout, or any I/O fault → the provider is unavailable.
            throw new AnswerGenerationException(AnswerGenerationFailure.Unavailable);
        }

        if (result.ExitCode != 0)
        {
            throw new AnswerGenerationException(AnswerGenerationFailure.Unavailable);
        }

        string answer = result.StdOut.Trim();
        if (answer.Length == 0)
        {
            throw new AnswerGenerationException(AnswerGenerationFailure.Unavailable);
        }

        yield return answer;
    }

    private string[] BuildArguments(string systemPrompt)
    {
        var arguments = new List<string> { "-p", "--output-format", "text" };
        if (!string.IsNullOrWhiteSpace(options.Model))
        {
            arguments.Add("--model");
            arguments.Add(options.Model);
        }

        arguments.Add("--append-system-prompt");
        arguments.Add(systemPrompt);
        return [.. arguments];
    }
}
