using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RagBook.Infrastructure.SharedContext.Persistence;
using RagBook.Modules.Chat;
using RagBook.Modules.Chat.Domain;
using RagBook.Modules.Chat.Errors;
using RagBook.Modules.Documents.Domain;
using RagBook.Shared.Results;
using RagBook.Shared.Sessions;

namespace RagBook.Infrastructure.SharedContext.Retrieval;

/// <summary>
/// Raw-SQL pgvector implementation of <see cref="IScopedRetriever"/> (US-13). It resolves/validates the
/// scope against the session, short-circuits an empty scope with a cheap <c>EXISTS</c> (no embedding, no
/// search), embeds the question through the US-06 <see cref="IEmbeddingProvider"/> seam, then runs a
/// cosine (<c>&lt;=&gt;</c>) similarity search pre-filtered by session + ready status + scope. The EF global
/// query filter does not apply to raw SQL, so the session filter is explicit (as in US-06); every user
/// value is a bound parameter (no interpolation).
/// </summary>
public sealed class ScopedRetriever(
    RagBookDbContext dbContext,
    IEmbeddingProvider embeddingProvider,
    ISessionContext sessionContext,
    IOptions<RagOptions> options)
    : IScopedRetriever
{
    private const int ReadyStatus = (int)DocumentStatus.Ready;

    /// <inheritdoc />
    public async Task<Result<ScopedRetrievalResult>> RetrieveAsync(
        ChatScope scope,
        string question,
        CancellationToken cancellationToken)
    {
        Guid session = sessionContext.UserSessionId;

        // 1. Resolve/validate the scope target against the session.
        string? scopePath = null;
        if (scope.Type == ChatScopeType.Folder)
        {
            scopePath = await GetFolderPathAsync(scope.TargetId!.Value, session, cancellationToken);
            if (scopePath is null)
            {
                return Result.Failure<ScopedRetrievalResult>(ChatErrors.ScopeNotFound);
            }
        }
        else if (scope.Type == ChatScopeType.Document && !await DocumentExistsAsync(scope.TargetId!.Value, session, cancellationToken))
        {
            return Result.Failure<ScopedRetrievalResult>(ChatErrors.ScopeNotFound);
        }

        // 2. Empty-scope short-circuit — before embedding or searching (AC-5).
        if (!await ScopeHasReadyChunksAsync(scope, scopePath, session, cancellationToken))
        {
            return ScopedRetrievalResult.Empty;
        }

        // 3. Embed the question (only now that the scope is non-empty).
        IReadOnlyList<float[]> embeddings = await embeddingProvider.EmbedBatchAsync([question], cancellationToken);
        float[] queryVector = embeddings[0];

        // 4. Scoped cosine similarity search, capped at TopK.
        IReadOnlyList<RetrievedChunk> matches =
            await SearchAsync(scope, scopePath, session, queryVector, options.Value.TopK, cancellationToken);

        return ScopedRetrievalResult.From(matches);
    }

    private async Task<string?> GetFolderPathAsync(Guid folderId, Guid session, CancellationToken cancellationToken)
    {
        await using DbCommand command = CreateCommand(
            "SELECT path FROM folders WHERE id = @id AND user_session_id = @session");
        AddParameter(command, "id", folderId);
        AddParameter(command, "session", session);

        await EnsureOpenAsync(command, cancellationToken);
        object? result = await command.ExecuteScalarAsync(cancellationToken);

        return result as string;
    }

    private async Task<bool> DocumentExistsAsync(Guid documentId, Guid session, CancellationToken cancellationToken)
    {
        await using DbCommand command = CreateCommand(
            "SELECT EXISTS(SELECT 1 FROM documents WHERE id = @id AND user_session_id = @session)");
        AddParameter(command, "id", documentId);
        AddParameter(command, "session", session);

        await EnsureOpenAsync(command, cancellationToken);

        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private async Task<bool> ScopeHasReadyChunksAsync(
        ChatScope scope,
        string? scopePath,
        Guid session,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = CreateCommand(
            $"""
             SELECT EXISTS(
               SELECT 1
               FROM chunks c
               JOIN documents d ON d.id = c.document_id
               LEFT JOIN folders f ON f.id = d.folder_id
               WHERE d.user_session_id = @session AND d.status = @ready AND ({ScopePredicate(scope)})
             )
             """);
        AddScopeParameters(command, scope, scopePath, session);

        await EnsureOpenAsync(command, cancellationToken);

        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        ChatScope scope,
        string? scopePath,
        Guid session,
        float[] queryVector,
        int topK,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = CreateCommand(
            $"""
             SELECT c.id, c.document_id, d.file_name, c.text, c.page_number,
                    c.embedding <=> CAST(@queryVec AS vector) AS distance
             FROM chunks c
             JOIN documents d ON d.id = c.document_id
             LEFT JOIN folders f ON f.id = d.folder_id
             WHERE d.user_session_id = @session AND d.status = @ready AND ({ScopePredicate(scope)})
             ORDER BY c.embedding <=> CAST(@queryVec AS vector)
             LIMIT @topK
             """);
        AddScopeParameters(command, scope, scopePath, session);
        AddParameter(command, "queryVec", ToVectorLiteral(queryVector));
        AddParameter(command, "topK", topK);

        await EnsureOpenAsync(command, cancellationToken);

        var matches = new List<RetrievedChunk>();
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            matches.Add(new RetrievedChunk(
                ChunkId: reader.GetGuid(0),
                DocumentId: reader.GetGuid(1),
                FileName: reader.GetString(2),
                Text: reader.GetString(3),
                PageNumber: reader.IsDBNull(4) ? null : reader.GetInt32(4),
                Distance: reader.GetDouble(5)));
        }

        return matches;
    }

    private static string ScopePredicate(ChatScope scope)
    {
        return scope.Type switch
        {
            ChatScopeType.Folder => "f.path LIKE @scopePath || '%'",
            ChatScopeType.Document => "d.id = @documentId",
            _ => "TRUE",
        };
    }

    private static void AddScopeParameters(DbCommand command, ChatScope scope, string? scopePath, Guid session)
    {
        AddParameter(command, "session", session);
        AddParameter(command, "ready", ReadyStatus);

        switch (scope.Type)
        {
            case ChatScopeType.Folder:
                AddParameter(command, "scopePath", scopePath!);
                break;
            case ChatScopeType.Document:
                AddParameter(command, "documentId", scope.TargetId!.Value);
                break;
            default:
                break;
        }
    }

    private DbCommand CreateCommand(string sql)
    {
        DbCommand command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;

        return command;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private async Task EnsureOpenAsync(DbCommand command, CancellationToken cancellationToken)
    {
        if (command.Connection!.State != ConnectionState.Open)
        {
            await command.Connection.OpenAsync(cancellationToken);
        }
    }

    private static string ToVectorLiteral(float[] vector)
    {
        return "[" + string.Join(",", vector.Select(value => value.ToString("R", CultureInfo.InvariantCulture))) + "]";
    }
}
