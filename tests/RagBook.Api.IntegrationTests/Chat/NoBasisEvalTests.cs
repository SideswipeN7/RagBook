using FluentAssertions;
using RagBook.Modules.Chat.Domain;
using Xunit;

namespace RagBook.Api.IntegrationTests.Chat;

/// <summary>
/// A host for the US-17 evaluation set that raises the retrieval similarity threshold to 0.9. The deterministic
/// stand-in embedding (dev/tests) is non-semantic — every unrelated text sits at cosine ≈0.75, so the production
/// 0.75 threshold cannot separate off-topic from on-topic here. At 0.9, an exact-text question (cosine 1.0) still
/// clears while off-topic questions (≈0.75) reliably fall below → the deterministic "matches below threshold"
/// path. The **production** threshold stays 0.75 (documented in README "Grounding i odmowa odpowiedzi"); a real
/// embedding separates off-topic far below it.
/// </summary>
public sealed class NoBasisEvalFactory : ChatAskApiFactory
{
    protected override double? SimilarityThresholdOverride => 0.9;
}

/// <summary>
/// US-17 AC-5 — the threshold/refusal evaluation set: ≥10 (question, expected state) pairs driven through the real
/// pipeline (fake generator, no real Anthropic). Off-topic questions must hit the deterministic no-grounds path
/// (no model call, no sources); an on-topic question the model refuses maps to `no_answer`; a produced one to
/// `answered`.
/// </summary>
public sealed class NoBasisEvalTests(NoBasisEvalFactory factory) : IClassFixture<NoBasisEvalFactory>
{
    private const string Urlop = "pracownikowi przysluguje 26 dni urlopu wypoczynkowego";
    private const string Wypowiedzenie = "okres wypowiedzenia umowy wynosi trzy miesiace";
    private const string Wynagrodzenie = "wynagrodzenie jest platne do dziesiatego dnia miesiaca";

    private enum Script
    {
        None, // off-topic — the generator must never be invoked
        Normal, // a produced answer
        Refusal, // the model returns the exact sentinel
    }

    private sealed record EvalCase(string Question, string ExpectedState, Script Script, bool Deterministic);

    [Fact]
    public async Task Should_ClassifyEachEvalPair_ToItsExpectedState()
    {
        // Arrange — a small on-topic corpus; off-topic questions stay unrelated to every chunk.
        var session = Guid.NewGuid();
        factory.StoreKey(session);
        await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "urlop.pdf", null, [(Urlop, 1)]);
        await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "wypowiedzenie.pdf", null, [(Wypowiedzenie, 2)]);
        await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "wynagrodzenie.pdf", null, [(Wynagrodzenie, 3)]);
        HttpClient client = SseEvents.CreateClient(factory, session);

        EvalCase[] cases =
        [
            // On-topic, answered (question repeats a chunk ⇒ cosine 1.0 clears; model produces an answer).
            new(Urlop, "answered", Script.Normal, Deterministic: false),
            new(Wypowiedzenie, "answered", Script.Normal, Deterministic: false),
            new(Wynagrodzenie, "answered", Script.Normal, Deterministic: false),
            // On-topic, but the model refuses with the sentinel ⇒ no_answer (prompt-refusal, grounds existed).
            new(Urlop, "no_answer", Script.Refusal, Deterministic: false),
            new(Wypowiedzenie, "no_answer", Script.Refusal, Deterministic: false),
            // Off-topic ⇒ below threshold ⇒ deterministic no-grounds: no model call, no sources.
            new("jaka jest stolica australii canberra kangur", "no_answer", Script.None, Deterministic: true),
            new("przepis na ciasto czekoladowe z bakaliami", "no_answer", Script.None, Deterministic: true),
            new("wyniki meczu pilki noznej wczoraj wieczorem", "no_answer", Script.None, Deterministic: true),
            new("kurs waluty euro do dolara na gieldzie", "no_answer", Script.None, Deterministic: true),
            new("prognoza pogody na weekend w gorach", "no_answer", Script.None, Deterministic: true),
        ];

        // Act + Assert — each pair, in isolation (reset the generator per row).
        foreach (EvalCase item in cases)
        {
            factory.Generator.Reset();
            if (item.Script == Script.Refusal)
            {
                factory.Generator.Deltas = [GroundingPrompt.RefusalPhrase];
            }

            IReadOnlyList<SseEvents.Event> events = await SseEvents.ReadAsync(await SseEvents.AskAsync(client, item.Question, "all"));

            events[^1].Name.Should().Be("done", "question '{0}' should complete", item.Question);
            events[^1].Data.Should().Contain($"\"state\":\"{item.ExpectedState}\"", "question '{0}' expected state {1}", item.Question, item.ExpectedState);

            if (item.Deterministic)
            {
                factory.Generator.Invoked.Should().BeFalse("off-topic question '{0}' must not call the model", item.Question);
                events.Should().NotContain(e => e.Name == "sources", "off-topic question '{0}' has no grounds", item.Question);
            }
            else
            {
                events.Should().Contain(e => e.Name == "sources", "on-topic question '{0}' had grounds", item.Question);
            }
        }
    }
}
