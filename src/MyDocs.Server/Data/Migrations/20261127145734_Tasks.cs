using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyDocs.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Tasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "lifecycle_date",
                table: "objects",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lifecycle_state",
                table: "objects",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "replaced_by_id",
                table: "objects",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    title_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    rule_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    responsible_person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    wait_until = table.Column<DateOnly>(type: "date", nullable: true),
                    outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    outcome_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_tasks_persons_responsible_person_id",
                        column: x => x.responsible_person_id,
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    object_id = table.Column<Guid>(type: "uuid", nullable: true),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_comments_objects_object_id",
                        column: x => x.object_id,
                        principalTable: "objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_comments_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_objects",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_objects", x => new { x.task_id, x.object_id });
                    table.ForeignKey(
                        name: "fk_task_objects_objects_object_id",
                        column: x => x.object_id,
                        principalTable: "objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_objects_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_objects_replaced_by_id",
                table: "objects",
                column: "replaced_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_comments_object_id",
                table: "comments",
                column: "object_id");

            migrationBuilder.CreateIndex(
                name: "ix_comments_task_id",
                table: "comments",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_objects_object_id",
                table: "task_objects",
                column: "object_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_responsible_person_id",
                table: "tasks",
                column: "responsible_person_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_rule_key",
                table: "tasks",
                column: "rule_key");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_state_due_on",
                table: "tasks",
                columns: new[] { "state", "due_on" });

            migrationBuilder.AddForeignKey(
                name: "fk_objects_objects_replaced_by_id",
                table: "objects",
                column: "replaced_by_id",
                principalTable: "objects",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // Bestehende Einträge bekommen den Anfangszustand ihres Musters (Lifecycle.InitialState).
            migrationBuilder.Sql("""
                UPDATE objects SET lifecycle_state = CASE
                    WHEN type_key IN ('bankAccount', 'depot', 'safeDeposit', 'loan', 'insurance', 'contract', 'domain', 'employment', 'pension') THEN 'active'
                    WHEN type_key IN ('idDocument', 'card', 'legalDocument') THEN 'valid'
                    WHEN type_key IN ('property', 'vehicle', 'asset', 'onlineAccount', 'cryptoWallet', 'device') THEN 'present'
                    ELSE NULL END
                WHERE lifecycle_state IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_objects_objects_replaced_by_id",
                table: "objects");

            migrationBuilder.DropTable(
                name: "comments");

            migrationBuilder.DropTable(
                name: "task_objects");

            migrationBuilder.DropTable(
                name: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_objects_replaced_by_id",
                table: "objects");

            migrationBuilder.DropColumn(
                name: "lifecycle_date",
                table: "objects");

            migrationBuilder.DropColumn(
                name: "lifecycle_state",
                table: "objects");

            migrationBuilder.DropColumn(
                name: "replaced_by_id",
                table: "objects");
        }
    }
}
