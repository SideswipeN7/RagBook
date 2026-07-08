using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RagBook.Modules.Documents.Domain;

namespace RagBook.Infrastructure.SharedContext.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="Document"/>, including the mandatory session index.</summary>
public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");

        builder.HasKey(document => document.Id);
        builder.Property(document => document.Id).HasColumnName("id");

        builder.Property(document => document.SizeBytes).HasColumnName("size_bytes").IsRequired();

        // Stored as int (the enum's underlying value) — minimal and forward-compatible with US-06.
        builder.Property(document => document.Status).HasColumnName("status").IsRequired();
        builder.Property(document => document.Origin).HasColumnName("origin").IsRequired();

        builder.Property(document => document.UserSessionId)
            .HasColumnName("user_session_id")
            .IsRequired();

        builder.Property(document => document.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(document => document.CreatedBy).HasColumnName("created_by").HasMaxLength(64).IsRequired();
        builder.Property(document => document.ModifiedAt).HasColumnName("modified_at");
        builder.Property(document => document.ModifiedBy).HasColumnName("modified_by").HasMaxLength(64);

        // Every session-scoped table is indexed by the session it belongs to (constitution §III).
        builder.HasIndex(document => document.UserSessionId)
            .HasDatabaseName("ix_documents_user_session_id");
    }
}
