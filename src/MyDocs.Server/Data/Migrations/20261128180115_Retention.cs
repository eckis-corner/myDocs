using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyDocs.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Retention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "retain_until",
                table: "documents",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "retain_until",
                table: "documents");
        }
    }
}
