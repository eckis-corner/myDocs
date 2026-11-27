using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyDocs.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Household : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "one_time_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_one_time_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_one_time_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "persons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_persons", x => x.id);
                    table.ForeignKey(
                        name: "fk_persons_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_one_time_tokens_token_hash",
                table: "one_time_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_one_time_tokens_user_id",
                table: "one_time_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_persons_user_id",
                table: "persons",
                column: "user_id",
                unique: true,
                filter: "user_id IS NOT NULL");

            // Konten aus der Zeit vor M1.3: Administratoren werden Mitglied, jedes Konto wird eine Person (77).
            migrationBuilder.Sql("""
                INSERT INTO roles (id, name, normalized_name, concurrency_stamp)
                SELECT gen_random_uuid(), r, upper(r), gen_random_uuid()::text
                FROM unnest(ARRAY['admin', 'member', 'guest']) AS r
                WHERE NOT EXISTS (SELECT 1 FROM roles WHERE normalized_name = upper(r));

                INSERT INTO user_roles (user_id, role_id)
                SELECT ur.user_id, m.id
                FROM user_roles ur
                JOIN roles a ON a.id = ur.role_id AND a.normalized_name = 'ADMIN'
                CROSS JOIN roles m
                WHERE m.normalized_name = 'MEMBER'
                  AND NOT EXISTS (SELECT 1 FROM user_roles x WHERE x.user_id = ur.user_id AND x.role_id = m.id);

                INSERT INTO persons (id, display_name, user_id, created_at, is_deleted)
                SELECT gen_random_uuid(), u.display_name, u.id, u.created_at, false
                FROM users u
                WHERE NOT EXISTS (SELECT 1 FROM persons p WHERE p.user_id = u.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "one_time_tokens");

            migrationBuilder.DropTable(
                name: "persons");
        }
    }
}
