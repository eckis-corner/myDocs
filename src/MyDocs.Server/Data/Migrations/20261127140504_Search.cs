using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace MyDocs.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Search : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.AddColumn<string>(
                name: "search_text",
                table: "objects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "search_text",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "objects",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('german', coalesce(search_text, ''))",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "documents",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('german', coalesce(search_text, ''))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_objects_search_text",
                table: "objects",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_objects_search_vector",
                table: "objects",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_documents_search_text",
                table: "documents",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_documents_search_vector",
                table: "documents",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_objects_search_text",
                table: "objects");

            migrationBuilder.DropIndex(
                name: "ix_objects_search_vector",
                table: "objects");

            migrationBuilder.DropIndex(
                name: "ix_documents_search_text",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "ix_documents_search_vector",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "objects");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "search_text",
                table: "objects");

            migrationBuilder.DropColumn(
                name: "search_text",
                table: "documents");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
