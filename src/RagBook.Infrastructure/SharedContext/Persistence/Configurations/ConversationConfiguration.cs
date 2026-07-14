using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RagBook.Modules.Chat.Domain;

namespace RagBook.Infrastructure.SharedContext.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="Conversation"/> (US-18). Scope is projected to <c>scope_type</c> (string) +
/// <c>scope_target_id</c> (nullable). The mandatory session index applies (constitution §III); the global query
/// filter is added centrally in <see cref="RagBookDbContext"/>.
/// </summary>
public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");

        builder.HasKey(conversation => conversation.Id);
        builder.Property(conversation => conversation.Id).HasColumnName("id");

        builder.Property(conversation => conversation.ScopeType)
            .HasColumnName("scope_type")
            .HasConversion<string>()
            .IsRequired();
        builder.Property(conversation => conversation.ScopeTargetId).HasColumnName("scope_target_id");

        builder.Property(conversation => conversation.Title).HasColumnName("title").IsRequired();

        builder.Property(conversation => conversation.UserSessionId)
            .HasColumnName("user_session_id")
            .IsRequired();

        builder.Property(conversation => conversation.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(conversation => conversation.CreatedBy).HasColumnName("created_by").HasMaxLength(64).IsRequired();
        builder.Property(conversation => conversation.ModifiedAt).HasColumnName("modified_at");
        builder.Property(conversation => conversation.ModifiedBy).HasColumnName("modified_by").HasMaxLength(64);

        builder.HasIndex(conversation => conversation.UserSessionId)
            .HasDatabaseName("ix_conversations_user_session_id");
    }
}
