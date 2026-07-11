using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace RagBook.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddChunks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The pgvector extension must exist before the vector(1024) column can be created (US-06).
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

            migrationBuilder.CreateTable(
                name: "chunks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    index = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    page_number = table.Column<int>(type: "integer", nullable: true),
                    embedding = table.Column<Vector>(type: "vector(1024)", nullable: false),
                    user_session_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chunks", x => x.id);
                    table.ForeignKey(
                        name: "FK_chunks_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chunks_document_id",
                table: "chunks",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_chunks_user_session_id",
                table: "chunks",
                column: "user_session_id");

            // One chunk per (document, index) — the idempotence backstop (US-06 AC-4).
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_chunks_document_index ON chunks (document_id, index);");

            // HNSW cosine index for similarity search (US-14 retrieval).
            migrationBuilder.Sql(
                "CREATE INDEX ix_chunks_embedding_hnsw ON chunks USING hnsw (embedding vector_cosine_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chunks");
        }
    }
}
