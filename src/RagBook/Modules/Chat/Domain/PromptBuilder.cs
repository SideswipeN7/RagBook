using System.Text;
using Microsoft.Extensions.Options;
using RagBook.Modules.Documents.Domain;

namespace RagBook.Modules.Chat.Domain;

/// <summary>
/// Builds the grounding context (US-14): numbers the passages <c>[1..K]</c> (most-relevant first), formats
/// each with its file + page, and drops the weakest whole passages until the assembled user message fits
/// <see cref="RagOptions.MaxContextChars"/>. The system instructions are the fixed <see cref="GroundingPrompt"/>.
/// </summary>
public sealed class PromptBuilder(IOptions<RagOptions> options) : IPromptBuilder
{
    /// <inheritdoc />
    public GroundedContext Build(string question, IReadOnlyList<RetrievedChunk> passages, IReadOnlyList<Message> history)
    {
        int maxContextChars = options.Value.MaxContextChars;
        var kept = passages.ToList();

        // Drop the weakest (last, highest-distance) passages until the numbered context fits the budget.
        while (kept.Count > 0 && ComposeUserPrompt(kept, question, history).Length > maxContextChars)
        {
            kept.RemoveAt(kept.Count - 1);
        }

        var sources = kept
            .Select((chunk, index) => new GroundingPassage(index + 1, chunk.DocumentId, chunk.FileName, chunk.PageNumber, chunk.Text, chunk.ChunkId))
            .ToList();

        return new GroundedContext(sources, GroundingPrompt.SystemInstructions, ComposeUserPrompt(kept, question, history));
    }

    private static string ComposeUserPrompt(IReadOnlyList<RetrievedChunk> passages, string question, IReadOnlyList<Message> history)
    {
        var builder = new StringBuilder();

        // Prepend the recent conversation turns (US-18) as context, before the freshly retrieved passages.
        if (history.Count > 0)
        {
            builder.Append("Wcześniejsza rozmowa:\n");
            foreach (Message message in history)
            {
                string speaker = message.Role == MessageRole.User ? "Użytkownik" : "Asystent";
                builder.Append(speaker).Append(": ").Append(message.Content).Append('\n');
            }

            builder.Append('\n');
        }

        for (int index = 0; index < passages.Count; index++)
        {
            RetrievedChunk chunk = passages[index];
            string page = chunk.PageNumber is int pageNumber ? $", s. {pageNumber}" : string.Empty;
            builder.Append('[').Append(index + 1).Append("] (").Append(chunk.FileName).Append(page).Append("): ")
                   .Append(chunk.Text).Append('\n');
        }

        builder.Append("\nPytanie: ").Append(question);

        return builder.ToString();
    }
}
