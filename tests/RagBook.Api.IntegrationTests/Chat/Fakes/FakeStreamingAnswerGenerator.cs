using System.Runtime.CompilerServices;
using RagBook.Modules.Chat.Domain;

namespace RagBook.Api.IntegrationTests.Chat.Fakes;

/// <summary>
/// Deterministic streaming <see cref="IAnswerGenerator"/> for US-14 endpoint tests — scripts the answer
/// deltas and can fail **before** or **after** the first delta, so a test can drive the pre-token vs
/// mid-stream error paths. Records whether it was invoked (AC-3: no call for insufficient grounding). No test
/// hits the real provider (§V).
/// </summary>
public sealed class FakeStreamingAnswerGenerator : IAnswerGenerator
{
    /// <summary>The deltas to stream (≥2 to prove incremental streaming).</summary>
    public IReadOnlyList<string> Deltas { get; set; } = ["Odpowiedź", " brzmi [1]."];

    /// <summary>When set, throw this failure before yielding any delta.</summary>
    public AnswerGenerationFailure? FailBeforeFirstDelta { get; set; }

    /// <summary>When set, throw this failure after the first delta (mid-stream).</summary>
    public AnswerGenerationFailure? FailAfterFirstDelta { get; set; }

    /// <summary>Optional delay between deltas — lets a test provoke a heartbeat (US-15 FR-010) or abort mid-stream.</summary>
    public TimeSpan DelayPerDelta { get; set; } = TimeSpan.Zero;

    /// <summary>True once <see cref="GenerateAsync"/> has been invoked.</summary>
    public bool Invoked { get; private set; }

    /// <summary>True once the generator observed cancellation (client disconnect / abort — US-15 AC-5).</summary>
    public bool CancellationObserved { get; private set; }

    /// <summary>The last grounded context passed to the generator — lets a test assert the prompt carried history (US-18).</summary>
    public GroundedContext? LastContext { get; private set; }

    /// <summary>Resets the recorded invocation and the script to defaults (call at the start of a test).</summary>
    public void Reset()
    {
        Invoked = false;
        CancellationObserved = false;
        LastContext = null;
        Deltas = ["Odpowiedź", " brzmi [1]."];
        FailBeforeFirstDelta = null;
        FailAfterFirstDelta = null;
        DelayPerDelta = TimeSpan.Zero;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> GenerateAsync(
        GroundedContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Invoked = true;
        LastContext = context;

        if (FailBeforeFirstDelta is AnswerGenerationFailure before)
        {
            throw new AnswerGenerationException(before);
        }

        await Task.Yield();

        for (int index = 0; index < Deltas.Count; index++)
        {
            yield return Deltas[index];

            if (DelayPerDelta > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(DelayPerDelta, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    CancellationObserved = true;

                    throw;
                }
            }

            if (index == 0 && FailAfterFirstDelta is AnswerGenerationFailure after)
            {
                throw new AnswerGenerationException(after);
            }
        }
    }
}
