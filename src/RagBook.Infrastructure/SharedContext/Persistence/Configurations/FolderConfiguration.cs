using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RagBook.Modules.Folders.Domain;

namespace RagBook.Infrastructure.SharedContext.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="Folder"/>. Maps the columns, the mandatory session index, and the
/// self-referencing parent foreign key with <see cref="DeleteBehavior.Restrict"/> — so the database
/// refuses to delete a folder that still has children (the AC-5 safety net). The case-insensitive
/// partial unique indexes on <c>LOWER(name)</c> and the <c>path text_pattern_ops</c> index are
/// functional/partial and cannot be modelled fluently, so they are created in the migration via SQL.
/// </summary>
public sealed class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Folder> builder)
    {
        builder.ToTable("folders");

        builder.HasKey(folder => folder.Id);
        builder.Property(folder => folder.Id).HasColumnName("id");

        builder.Property(folder => folder.Name).HasColumnName("name").IsRequired();
        builder.Property(folder => folder.ParentId).HasColumnName("parent_id");
        builder.Property(folder => folder.Path).HasColumnName("path").IsRequired();

        builder.Property(folder => folder.UserSessionId)
            .HasColumnName("user_session_id")
            .IsRequired();

        builder.Property(folder => folder.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(folder => folder.CreatedBy).HasColumnName("created_by").HasMaxLength(64).IsRequired();
        builder.Property(folder => folder.ModifiedAt).HasColumnName("modified_at");
        builder.Property(folder => folder.ModifiedBy).HasColumnName("modified_by").HasMaxLength(64);

        // Every session-scoped table is indexed by the session it belongs to (constitution §III).
        builder.HasIndex(folder => folder.UserSessionId)
            .HasDatabaseName("ix_folders_user_session_id");

        // Self-referencing parent FK, no cascade: deleting a folder with children is refused by the DB.
        builder.HasOne<Folder>()
            .WithMany()
            .HasForeignKey(folder => folder.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
