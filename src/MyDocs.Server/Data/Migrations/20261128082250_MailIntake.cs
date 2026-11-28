using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyDocs.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class MailIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "mail_body_always",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<List<string>>(
                name: "mail_senders",
                table: "users",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<bool>(
                name: "has_macros",
                table: "documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "processed_mails",
                columns: table => new
                {
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_mails", x => x.hash);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_mails");

            migrationBuilder.DropColumn(
                name: "mail_body_always",
                table: "users");

            migrationBuilder.DropColumn(
                name: "mail_senders",
                table: "users");

            migrationBuilder.DropColumn(
                name: "has_macros",
                table: "documents");
        }
    }
}
