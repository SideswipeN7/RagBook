using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RagBook.Modules.Session.Domain;
using RagBook.Modules.Session.Features.CreateResource;

namespace RagBook.Infrastructure.SharedContext.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="SessionResource"/>, including the mandatory session index.</summary>
public sealed class SessionResourceConfiguration : IEntityTypeConfiguration<SessionResource>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SessionResource> builder)
    {
        builder.ToTable("session_resources");

        builder.HasKey(resource => resource.Id);
        builder.Property(resource => resource.Id).HasColumnName("id");

        builder.Property(resource => resource.Name)
            .HasColumnName("name")
            .HasMaxLength(CreateResourceCommand.MaxNameLength)
            .IsRequired();

        builder.Property(resource => resource.UserSessionId)
            .HasColumnName("user_session_id")
            .IsRequired();

        builder.Property(resource => resource.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(resource => resource.CreatedBy).HasColumnName("created_by").HasMaxLength(64).IsRequired();
        builder.Property(resource => resource.ModifiedAt).HasColumnName("modified_at");
        builder.Property(resource => resource.ModifiedBy).HasColumnName("modified_by").HasMaxLength(64);

        // Every session-scoped table is indexed by the session it belongs to (constitution §III).
        builder.HasIndex(resource => resource.UserSessionId)
            .HasDatabaseName("ix_session_resources_user_session_id");
    }
}
