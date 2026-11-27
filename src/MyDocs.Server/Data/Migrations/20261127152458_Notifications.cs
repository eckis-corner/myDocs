using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyDocs.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Notifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "digest_hour",
                table: "users",
                type: "integer",
                nullable: true);

            // Bestehende Konten bekommen die tägliche Zusammenfassung um 7 Uhr (47), wie neue.
            migrationBuilder.Sql("UPDATE users SET digest_hour = 7;");

            migrationBuilder.AddColumn<string>(
                name: "language",
                table: "users",
                type: "character varying(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "de");

            migrationBuilder.AddColumn<DateOnly>(
                name: "last_digest_on",
                table: "users",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ntfy_topic",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "mail_outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    template = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    parameters = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mail_outbox", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mail_outbox_next_attempt_at",
                table: "mail_outbox",
                column: "next_attempt_at",
                filter: "sent_at IS NULL AND failed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mail_outbox");

            migrationBuilder.DropColumn(
                name: "digest_hour",
                table: "users");

            migrationBuilder.DropColumn(
                name: "language",
                table: "users");

            migrationBuilder.DropColumn(
                name: "last_digest_on",
                table: "users");

            migrationBuilder.DropColumn(
                name: "ntfy_topic",
                table: "users");
        }
    }
}
