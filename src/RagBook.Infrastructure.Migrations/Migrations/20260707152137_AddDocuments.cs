using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RagBook.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    origin = table.Column<int>(type: "integer", nullable: false),
                    user_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_documents_user_session_id",
                table: "documents",
                column: "user_session_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documents");
        }
    }
}
