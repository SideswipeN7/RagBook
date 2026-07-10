using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RagBook.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ExtendDocumentsForUpload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "chunk_count",
                table: "documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "content_type",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "file_name",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "folder_id",
                table: "documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "storage_path",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "uploaded_at",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_documents_folder_id",
                table: "documents",
                column: "folder_id");

            migrationBuilder.AddForeignKey(
                name: "FK_documents_folders_folder_id",
                table: "documents",
                column: "folder_id",
                principalTable: "folders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Case-insensitive per-folder file-name uniqueness (US-04 AC-5). Two partial indexes because
            // NULL folder_ids (root) are distinct in a plain unique index; "file_name IS NOT NULL"
            // excludes the US-05-minimal seed rows (documents with no file).
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_documents_root_file ON documents (user_session_id, LOWER(file_name)) " +
                "WHERE folder_id IS NULL AND file_name IS NOT NULL;");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_documents_folder_file ON documents (folder_id, LOWER(file_name)) " +
                "WHERE folder_id IS NOT NULL AND file_name IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_documents_folder_file;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_documents_root_file;");

            migrationBuilder.DropForeignKey(
                name: "FK_documents_folders_folder_id",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "ix_documents_folder_id",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "chunk_count",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "content_type",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "file_name",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "folder_id",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "storage_path",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "uploaded_at",
                table: "documents");
        }
    }
}
