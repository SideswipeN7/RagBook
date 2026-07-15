using Microsoft.EntityFrameworkCore;
using RagBook.Modules.Chat.Domain;

namespace RagBook.Infrastructure.SharedContext.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IConversationRepository"/> (US-18). All reads flow through the context's
/// global query filter, so they are automatically scoped to the current session (a cross-session id reads as
/// <c>null</c> / empty → 404). The owning session is stamped centrally on save from the ambient session context.
/// </summary>
public sealed class ConversationRepository(RagBookDbContext dbContext) : IConversationRepository
{
    /// <inheritdoc />
    public Task AddAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        dbContext.Conversations.Add(conversation);

        return dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<Conversation?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Conversations.FirstOrDefaultAsync(conversation => conversation.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Conversation>> ListForSessionAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Conversations
            .AsNoTracking()
            .OrderByDescending(conversation => conversation.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Message>> ListMessagesAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        return await dbContext.Messages
            .AsNoTracking()
            .Where(message => message.ConversationId == conversationId)
            .OrderBy(message => message.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task AddMessageAsync(Message message, CancellationToken cancellationToken)
    {
        dbContext.Messages.Add(message);

        return dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task RemoveAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        dbContext.Conversations.Remove(conversation);

        return dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
