using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RagBook.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "folders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    path = table.Column<string>(type: "text", nullable: false),
                    user_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_folders", x => x.id);
                    table.ForeignKey(
                        name: "FK_folders_folders_parent_id",
                        column: x => x.parent_id,
                        principalTable: "folders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_folders_parent_id",
                table: "folders",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_folders_user_session_id",
                table: "folders",
                column: "user_session_id");

            // Case-insensitive per-parent name uniqueness (AC-3). Two partial indexes because Postgres
            // treats NULL parent_ids as distinct, so a single composite constraint would not guard root
            // duplicates. LOWER(name) gives case-insensitive matching without the citext extension.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_folders_root_name ON folders (user_session_id, LOWER(name)) " +
                "WHERE parent_id IS NULL;");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_folders_child_name ON folders (user_session_id, parent_id, LOWER(name)) " +
                "WHERE parent_id IS NOT NULL;");

            // Prefix index for subtree queries (path LIKE prefix || '%'); text_pattern_ops is required
            // for LIKE-prefix index usage under non-C collations.
            migrationBuilder.Sql(
                "CREATE INDEX ix_folders_path ON folders (path text_pattern_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_folders_path;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_folders_child_name;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_folders_root_name;");

            migrationBuilder.DropTable(
                name: "folders");
        }
    }
}
