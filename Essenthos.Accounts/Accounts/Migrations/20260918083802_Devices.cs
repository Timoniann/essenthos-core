using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Accounts.Migrations
{
    /// <inheritdoc />
    public partial class Devices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "device_id",
                table: "session",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "account_email",
                columns: table => new
                {
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_email", x => x.email);
                    table.ForeignKey(
                        name: "fk_account_email_account_account_id",
                        column: x => x.account_id,
                        principalTable: "account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "device",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    os = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    browser = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    model = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    settings = table.Column<string>(type: "jsonb", nullable: true),
                    settings_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device", x => x.id);
                    table.ForeignKey(
                        name: "fk_device_account_account_id",
                        column: x => x.account_id,
                        principalTable: "account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reading",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    book = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    chapter = table.Column<int>(type: "integer", nullable: false),
                    corpora = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reading", x => x.id);
                    table.ForeignKey(
                        name: "fk_reading_device_device_id",
                        column: x => x.device_id,
                        principalTable: "device",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_session_device_id",
                table: "session",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_email_account_id",
                table: "account_email",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_account_id",
                table: "device",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_revision",
                table: "device",
                column: "revision");

            migrationBuilder.CreateIndex(
                name: "ix_reading_account_id_at",
                table: "reading",
                columns: new[] { "account_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_reading_device_id_at",
                table: "reading",
                columns: new[] { "device_id", "at" });

            migrationBuilder.AddForeignKey(
                name: "fk_session_devices_device_id",
                table: "session",
                column: "device_id",
                principalTable: "device",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_session_devices_device_id",
                table: "session");

            migrationBuilder.DropTable(
                name: "account_email");

            migrationBuilder.DropTable(
                name: "reading");

            migrationBuilder.DropTable(
                name: "device");

            migrationBuilder.DropIndex(
                name: "ix_session_device_id",
                table: "session");

            migrationBuilder.DropColumn(
                name: "device_id",
                table: "session");
        }
    }
}
