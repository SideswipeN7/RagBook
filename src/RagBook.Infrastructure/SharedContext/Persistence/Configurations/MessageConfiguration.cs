using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RagBook.Modules.Chat.Domain;

namespace RagBook.Infrastructure.SharedContext.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="Message"/> (US-18). <c>role</c>/<c>state</c> persist as strings; <c>sources_json</c>
/// is <c>jsonb</c> (the US-16 <c>SourceDto[]</c> shape). The FK to <c>conversations</c> cascades on delete so
/// deleting a conversation removes its messages. Session index + global filter apply (constitution §III).
/// </summary>
public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).HasColumnName("id");

        builder.Property(message => message.ConversationId).HasColumnName("conversation_id").IsRequired();

        builder.Property(message => message.Role)
            .HasColumnName("role")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(message => message.Content).HasColumnName("content").IsRequired();

        builder.Property(message => message.State)
            .HasColumnName("state")
            .HasConversion<string?>();

        builder.Property(message => message.SourcesJson).HasColumnName("sources_json").HasColumnType("jsonb");

        builder.Property(message => message.UserSessionId)
            .HasColumnName("user_session_id")
            .IsRequired();

        builder.Property(message => message.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(message => message.CreatedBy).HasColumnName("created_by").HasMaxLength(64).IsRequired();
        builder.Property(message => message.ModifiedAt).HasColumnName("modified_at");
        builder.Property(message => message.ModifiedBy).HasColumnName("modified_by").HasMaxLength(64);

        builder.HasIndex(message => message.UserSessionId)
            .HasDatabaseName("ix_messages_user_session_id");

        builder.HasIndex(message => message.ConversationId)
            .HasDatabaseName("ix_messages_conversation_id");

        // Deleting a conversation cascades to its messages (US-18 hard delete).
        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(message => message.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
