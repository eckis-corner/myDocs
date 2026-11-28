using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyDocs.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Inbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "in_inbox",
                table: "documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "inbox_person_id",
                table: "documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "documents",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "documents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "source_detail",
                table: "documents",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_documents_in_inbox_inbox_person_id",
                table: "documents",
                columns: new[] { "in_inbox", "inbox_person_id" },
                filter: "in_inbox");

            migrationBuilder.CreateIndex(
                name: "ix_documents_inbox_person_id",
                table: "documents",
                column: "inbox_person_id");

            migrationBuilder.AddForeignKey(
                name: "fk_documents_persons_inbox_person_id",
                table: "documents",
                column: "inbox_person_id",
                principalTable: "persons",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_documents_persons_inbox_person_id",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "ix_documents_in_inbox_inbox_person_id",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "ix_documents_inbox_person_id",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "in_inbox",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "inbox_person_id",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "source",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "source_detail",
                table: "documents");
        }
    }
}
