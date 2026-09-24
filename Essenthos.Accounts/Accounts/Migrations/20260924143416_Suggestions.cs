using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Accounts.Migrations
{
    /// <inheritdoc />
    public partial class Suggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "admin",
                table: "account",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "admin_action",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    suggestion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    before = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    after = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_action", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "suggestion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    fields = table.Column<string>(type: "jsonb", nullable: false),
                    links = table.Column<List<string>>(type: "text[]", nullable: false),
                    language = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    has_link = table.Column<bool>(type: "boolean", nullable: false),
                    locale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    assigned_to = table.Column<Guid>(type: "uuid", nullable: true),
                    search = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_reply_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    author_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suggestion", x => x.id);
                    table.ForeignKey(
                        name: "fk_suggestion_account_account_id",
                        column: x => x.account_id,
                        principalTable: "account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_suggestion_account_assigned_to",
                        column: x => x.assigned_to,
                        principalTable: "account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "suggestion_message",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    suggestion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suggestion_message", x => x.id);
                    table.ForeignKey(
                        name: "fk_suggestion_message_account_author_id",
                        column: x => x.author_id,
                        principalTable: "account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_suggestion_message_suggestion_suggestion_id",
                        column: x => x.suggestion_id,
                        principalTable: "suggestion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_admin_action_account_id_at",
                table: "admin_action",
                columns: new[] { "account_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_action_at",
                table: "admin_action",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_admin_action_suggestion_id_at",
                table: "admin_action",
                columns: new[] { "suggestion_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_suggestion_account_id_created_at",
                table: "suggestion",
                columns: new[] { "account_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_suggestion_assigned_to",
                table: "suggestion",
                column: "assigned_to");

            migrationBuilder.CreateIndex(
                name: "ix_suggestion_status_last_activity_at",
                table: "suggestion",
                columns: new[] { "status", "last_activity_at" });

            migrationBuilder.CreateIndex(
                name: "ix_suggestion_message_author_id_at",
                table: "suggestion_message",
                columns: new[] { "author_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_suggestion_message_suggestion_id_at",
                table: "suggestion_message",
                columns: new[] { "suggestion_id", "at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_action");

            migrationBuilder.DropTable(
                name: "suggestion_message");

            migrationBuilder.DropTable(
                name: "suggestion");

            migrationBuilder.DropColumn(
                name: "admin",
                table: "account");
        }
    }
}
