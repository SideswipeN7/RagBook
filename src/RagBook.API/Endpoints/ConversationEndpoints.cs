using System.Text.Json;
using RagBook.API.ProblemDetails;
using RagBook.Modules.Chat.Domain;
using RagBook.Modules.Chat.Features.Conversations;
using RagBook.Modules.Chat.Features.Conversations.CreateConversation;
using RagBook.Modules.Chat.Features.Conversations.DeleteConversation;
using RagBook.Modules.Chat.Features.Conversations.GetConversation;
using RagBook.Modules.Chat.Features.Conversations.ListConversations;
using RagBook.Shared.Results;
using Wolverine;

namespace RagBook.API.Endpoints;

/// <summary>
/// Maps conversation management (US-18): create / list / get / delete. Every operation is scoped to the current
/// session by the persistence layer; another session's conversation resolves to 404 (never 403), consistent with
/// US-01. The ask endpoint (US-14) carries the <c>conversationId</c> and persists the turn.
/// </summary>
public static class ConversationEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Maps <c>POST/GET /api/conversations</c>, <c>GET/DELETE /api/conversations/{id}</c>.</summary>
    public static IEndpointRouteBuilder MapConversationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/conversations");

        group.MapPost("/", async (CreateConversationRequest request, IMessageBus bus, CancellationToken cancellationToken) =>
        {
            (ChatScopeType type, Guid? targetId) = MapScope(request.Scope);
            ConversationSummary summary = await bus.InvokeAsync<ConversationSummary>(
                new CreateConversationCommand(type, targetId),
                cancellationToken);

            return Results.Created($"/api/conversations/{summary.Id}", summary);
        });

        group.MapGet("/", async (IMessageBus bus, CancellationToken cancellationToken) =>
        {
            IReadOnlyList<ConversationSummary> conversations =
                await bus.InvokeAsync<IReadOnlyList<ConversationSummary>>(new ListConversationsQuery(), cancellationToken);

            return Results.Ok(conversations);
        });

        group.MapGet("/{id:guid}", async (Guid id, IMessageBus bus, CancellationToken cancellationToken) =>
        {
            Result<ConversationDetail> result =
                await bus.InvokeAsync<Result<ConversationDetail>>(new GetConversationQuery(id), cancellationToken);

            if (result.IsFailure)
            {
                return ProblemResults.Problem(result.Error);
            }

            ConversationDetail detail = result.Value;
            var messages = detail.Messages
                .Select(message => new MessageResponse(
                    message.Id,
                    message.Role,
                    message.Content,
                    message.State,
                    ParseSources(message.SourcesJson),
                    message.CreatedAt))
                .ToList();

            return Results.Ok(new ConversationDetailResponse(
                detail.Summary.Id,
                detail.Summary.Title,
                detail.Summary.ScopeType,
                detail.Summary.ScopeTargetId,
                detail.Summary.CreatedAt,
                messages));
        });

        group.MapDelete("/{id:guid}", async (Guid id, IMessageBus bus, CancellationToken cancellationToken) =>
        {
            Result result = await bus.InvokeAsync<Result>(new DeleteConversationCommand(id), cancellationToken);

            return result.IsSuccess ? Results.NoContent() : ProblemResults.Problem(result.Error);
        });

        return endpoints;
    }

    private static IReadOnlyList<SourceDto>? ParseSources(string? sourcesJson)
    {
        return string.IsNullOrWhiteSpace(sourcesJson)
            ? null
            : JsonSerializer.Deserialize<List<SourceDto>>(sourcesJson, JsonOptions);
    }

    private static (ChatScopeType Type, Guid? TargetId) MapScope(ScopeDto? scope)
    {
        return scope?.Type?.ToLowerInvariant() switch
        {
            "folder" when scope.TargetId is Guid folderId => (ChatScopeType.Folder, folderId),
            "document" when scope.TargetId is Guid documentId => (ChatScopeType.Document, documentId),
            _ => (ChatScopeType.All, null),
        };
    }
}
