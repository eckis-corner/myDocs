using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace MyDocs.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Processing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "content_text",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "content_vector",
                table: "documents",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('german', coalesce(content_text, ''))",
                stored: true);

            migrationBuilder.CreateTable(
                name: "blob_derivatives",
                columns: table => new
                {
                    source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    blob_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_blob_derivatives", x => new { x.source_hash, x.kind });
                    table.ForeignKey(
                        name: "fk_blob_derivatives_blobs_blob_hash",
                        column: x => x.blob_hash,
                        principalTable: "blobs",
                        principalColumn: "hash",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_blob_derivatives_blobs_source_hash",
                        column: x => x.source_hash,
                        principalTable: "blobs",
                        principalColumn: "hash",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "blob_texts",
                columns: table => new
                {
                    source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_blob_texts", x => x.source_hash);
                    table.ForeignKey(
                        name: "fk_blob_texts_blobs_source_hash",
                        column: x => x.source_hash,
                        principalTable: "blobs",
                        principalColumn: "hash",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "processing_jobs",
                columns: table => new
                {
                    source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processing_jobs", x => x.source_hash);
                    table.ForeignKey(
                        name: "fk_processing_jobs_blobs_source_hash",
                        column: x => x.source_hash,
                        principalTable: "blobs",
                        principalColumn: "hash",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_documents_content_text",
                table: "documents",
                column: "content_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_documents_content_vector",
                table: "documents",
                column: "content_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_blob_derivatives_blob_hash",
                table: "blob_derivatives",
                column: "blob_hash");

            migrationBuilder.CreateIndex(
                name: "ix_processing_jobs_state_next_attempt_at",
                table: "processing_jobs",
                columns: new[] { "state", "next_attempt_at" });

            // Vorhandene Dokumente kommen in die Warteschlange (einmal je Inhalt, Dateiname der ersten Version).
            migrationBuilder.Sql("""
                INSERT INTO processing_jobs (source_hash, file_name, state, attempts, next_attempt_at, created_at)
                SELECT DISTINCT ON (blob_hash) blob_hash, file_name, 'pending', 0, now(), now()
                FROM document_versions
                ORDER BY blob_hash, created_at
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blob_derivatives");

            migrationBuilder.DropTable(
                name: "blob_texts");

            migrationBuilder.DropTable(
                name: "processing_jobs");

            migrationBuilder.DropIndex(
                name: "ix_documents_content_text",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "ix_documents_content_vector",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "content_vector",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "content_text",
                table: "documents");
        }
    }
}
