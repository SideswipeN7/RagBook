using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RagBook.Modules.Chat;
using RagBook.Modules.Chat.Domain;
using RagBook.Modules.Settings.Domain;
using RagBook.Shared.Results;

namespace RagBook.Infrastructure.SharedContext.Providers.Cli;

/// <summary>
/// The default <see cref="IAnswerGenerator"/> — picks the backend per request (US-22). A usable key always wins:
/// key present → the Anthropic generator (unchanged BYOK/demo behaviour). No usable key + CLI mode enabled → the
/// keyless <see cref="ClaudeCliAnswerGenerator"/>. No usable key + CLI mode off → the existing key-missing failure
/// (<see cref="AnswerGenerationFailure.InvalidKey"/> for a session ask, <see cref="AnswerGenerationFailure.Unavailable"/>
/// for a demo ask — matching what the Anthropic generator would have thrown). The concrete generators are resolved
/// by DI key so both are injectable + fakeable in the router's unit test.
/// </summary>
public sealed class RoutingAnswerGenerator(
    [FromKeyedServices("anthropic")] IAnswerGenerator anthropic,
    [FromKeyedServices("cli")] IAnswerGenerator cli,
    IAnthropicClientFactory clientFactory,
    IOptions<ClaudeCliOptions> cliOptions)
    : IAnswerGenerator
{
    /// <inheritdoc />
    public IAsyncEnumerable<string> GenerateAsync(GroundedContext context, CancellationToken cancellationToken)
    {
        Result<AnthropicClientHandle> handle = context.IsDemo
            ? clientFactory.CreateForDemo()
            : clientFactory.CreateForSession();

        if (handle.IsSuccess)
        {
            return anthropic.GenerateAsync(context, cancellationToken);
        }

        if (cliOptions.Value.Enabled)
        {
            return cli.GenerateAsync(context, cancellationToken);
        }

        return Throw(context.IsDemo);
    }

    private static async IAsyncEnumerable<string> Throw(bool isDemo)
    {
        await Task.CompletedTask;
        throw new AnswerGenerationException(
            isDemo ? AnswerGenerationFailure.Unavailable : AnswerGenerationFailure.InvalidKey);
#pragma warning disable CS0162 // Unreachable — satisfies the iterator's yield-type inference.
        yield break;
#pragma warning restore CS0162
    }
}
