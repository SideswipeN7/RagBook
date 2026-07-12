using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RagBook.Modules.Documents.Domain;

namespace RagBook.Infrastructure.SharedContext.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="Chunk"/> (US-06). The pgvector <c>embedding</c> column is **not**
/// mapped by EF (the Npgsql EF vector plugin is incompatible with EF Core 10); it is written via raw SQL
/// in <c>ChunkRepository</c> using a text→<c>vector</c> cast, and the column, unique
/// <c>(document_id, index)</c>, and HNSW index are created in the migration. The document FK
/// cascade-deletes chunks (US-08).
/// </summary>
public sealed class ChunkConfiguration : IEntityTypeConfiguration<Chunk>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Chunk> builder)
    {
        builder.ToTable("chunks");

        builder.HasKey(chunk => chunk.Id);
        builder.Property(chunk => chunk.Id).HasColumnName("id");
        builder.Property(chunk => chunk.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(chunk => chunk.UserSessionId).HasColumnName("user_session_id").IsRequired();
        builder.Property(chunk => chunk.Index).HasColumnName("index").IsRequired();
        builder.Property(chunk => chunk.Text).HasColumnName("text").IsRequired();
        builder.Property(chunk => chunk.PageNumber).HasColumnName("page_number");

        // The vector column is written/read via raw SQL (see ChunkRepository) — EF does not map it.
        builder.Ignore(chunk => chunk.Embedding);

        builder.HasIndex(chunk => chunk.UserSessionId).HasDatabaseName("ix_chunks_user_session_id");

        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(chunk => chunk.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
