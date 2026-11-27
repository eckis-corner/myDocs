using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyDocs.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Reviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "escalation_level",
                table: "tasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "postponed_count",
                table: "tasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "last_reviewed_on",
                table: "objects",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "next_review_on",
                table: "objects",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "overdue_since",
                table: "objects",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "review_interval_months",
                table: "objects",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "escalation_level",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "postponed_count",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "last_reviewed_on",
                table: "objects");

            migrationBuilder.DropColumn(
                name: "next_review_on",
                table: "objects");

            migrationBuilder.DropColumn(
                name: "overdue_since",
                table: "objects");

            migrationBuilder.DropColumn(
                name: "review_interval_months",
                table: "objects");
        }
    }
}
