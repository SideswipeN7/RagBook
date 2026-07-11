using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Folders.Domain;

namespace RagBook.Infrastructure.SharedContext.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="Document"/>, including the mandatory session index and, from
/// US-04, the folder association and upload metadata.</summary>
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

        // US-04 upload metadata. Nullable so the US-05-minimal seed rows (no file) remain valid.
        builder.Property(document => document.FolderId).HasColumnName("folder_id");
        builder.Property(document => document.FileName).HasColumnName("file_name");
        builder.Property(document => document.ContentType).HasColumnName("content_type");
        builder.Property(document => document.StoragePath).HasColumnName("storage_path");
        builder.Property(document => document.UploadedAt).HasColumnName("uploaded_at");
        builder.Property(document => document.ChunkCount).HasColumnName("chunk_count").IsRequired().HasDefaultValue(0);

        // US-07 display field, forward-looking — populated by US-06 on a failed transition.
        builder.Property(document => document.FailureReason).HasColumnName("failure_reason");

        builder.HasIndex(document => document.FolderId).HasDatabaseName("ix_documents_folder_id");

        // A document points at a folder; the folder cannot be deleted while it holds documents (US-09 AC-5).
        builder.HasOne<Folder>()
            .WithMany()
            .HasForeignKey(document => document.FolderId)
            .OnDelete(DeleteBehavior.Restrict);

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
