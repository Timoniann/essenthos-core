using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheStepLoadsMarksAreGone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "carry_mark");

            migrationBuilder.DropTable(
                name: "load_step_mark");

            migrationBuilder.DropTable(
                name: "table_fingerprint");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "carry_mark",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    link_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carry_mark", x => x.id);
                    table.CheckConstraint("ck_carry_mark_one_row", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "load_step_mark",
                columns: table => new
                {
                    step = table.Column<string>(type: "text", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    input_digest = table.Column<string>(type: "text", nullable: false),
                    inputs = table.Column<string>(type: "jsonb", nullable: false),
                    output_digest = table.Column<string>(type: "text", nullable: false),
                    reads = table.Column<string[]>(type: "text[]", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_load_step_mark", x => x.step);
                },
                comment: "What each step of the load last ran on: digests of its code, files and the tables it read, and of the tables it left. A step whose inputs digest the same is skipped. Derived; deleting a row only costs running that step once more.");

            migrationBuilder.CreateTable(
                name: "table_fingerprint",
                columns: table => new
                {
                    table_name = table.Column<string>(type: "text", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    columns = table.Column<string>(type: "text", nullable: false),
                    file_node = table.Column<long>(type: "bigint", nullable: false),
                    fingerprint = table.Column<string>(type: "text", nullable: false),
                    rows = table.Column<long>(type: "bigint", nullable: false),
                    writes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_table_fingerprint", x => x.table_name);
                },
                comment: "Each table's rows summed (count and a 64-bit hash of every row), with the table's file, its columns and the count of transactions that had written it, so the sum is taken again only after a write. Derived; deleting a row only costs taking that sum again.");
        }
    }
}
