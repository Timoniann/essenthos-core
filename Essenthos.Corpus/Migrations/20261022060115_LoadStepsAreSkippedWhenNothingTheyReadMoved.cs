using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class LoadStepsAreSkippedWhenNothingTheyReadMoved : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "load_step_mark",
                columns: table => new
                {
                    step = table.Column<string>(type: "text", nullable: false),
                    input_digest = table.Column<string>(type: "text", nullable: false),
                    output_digest = table.Column<string>(type: "text", nullable: false),
                    reads = table.Column<string[]>(type: "text[]", nullable: true),
                    inputs = table.Column<string>(type: "jsonb", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                    file_node = table.Column<long>(type: "bigint", nullable: false),
                    columns = table.Column<string>(type: "text", nullable: false),
                    writes = table.Column<long>(type: "bigint", nullable: false),
                    rows = table.Column<long>(type: "bigint", nullable: false),
                    fingerprint = table.Column<string>(type: "text", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_table_fingerprint", x => x.table_name);
                },
                comment: "Each table's rows summed (count and a 64-bit hash of every row), with the table's file, its columns and the count of transactions that had written it, so the sum is taken again only after a write. Derived; deleting a row only costs taking that sum again.");

            migrationBuilder.CreateTable(
                name: "table_write",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    table_name = table.Column<string>(type: "text", nullable: false),
                    weight = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_table_write", x => x.id);
                },
                comment: "One row per table per transaction that wrote it, added by the statement trigger note_table_write in the same transaction; folded per table by the load, the weights summed.");

            migrationBuilder.CreateIndex(
                name: "ix_table_write_table_name",
                table: "table_write",
                column: "table_name");

            // Once per table per transaction that writes it, in that transaction: a write rolled back
            // takes its row with it, and one committed is counted exactly when it can be seen. The
            // setting is local to the transaction, so its later statements on the same table cost a
            // lookup and no row.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION note_table_write() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF current_setting('essenthos.wrote_' || TG_TABLE_NAME, true) IS DISTINCT FROM '1' THEN
                        INSERT INTO table_write (table_name, weight) VALUES (TG_TABLE_NAME, 1);
                        PERFORM set_config('essenthos.wrote_' || TG_TABLE_NAME, '1', true);
                    END IF;
                    RETURN NULL;
                END $$;
                """);

            // Puts the trigger on every table of the schema that lacks it, or has it switched off, and
            // names those tables: what they hold may have moved unseen. The load calls it as it starts,
            // so a table a later migration adds is watched without that migration remembering to.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION watch_table_writes() RETURNS SETOF text
                LANGUAGE plpgsql AS $$
                DECLARE t record;
                BEGIN
                    FOR t IN
                        SELECT c.relname, g.oid AS trigger_oid
                        FROM pg_class c
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        LEFT JOIN pg_trigger g ON g.tgrelid = c.oid AND g.tgname = 'table_written'
                        WHERE n.nspname = 'public' AND c.relkind = 'r'
                          AND c.relname NOT IN ('table_write', 'table_fingerprint', 'load_step_mark', '__EFMigrationsHistory')
                          AND (g.oid IS NULL OR g.tgenabled <> 'O')
                        ORDER BY c.relname
                    LOOP
                        IF t.trigger_oid IS NULL THEN
                            EXECUTE format(
                                'CREATE TRIGGER table_written AFTER INSERT OR UPDATE OR DELETE OR TRUNCATE ON public.%I '
                                || 'FOR EACH STATEMENT EXECUTE FUNCTION note_table_write()', t.relname);
                        ELSE
                            EXECUTE format('ALTER TABLE public.%I ENABLE TRIGGER table_written', t.relname);
                        END IF;
                        RETURN NEXT t.relname;
                    END LOOP;
                END $$;
                """);

            migrationBuilder.Sql("SELECT count(*) FROM watch_table_writes();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE t record;
                BEGIN
                    FOR t IN SELECT c.relname FROM pg_trigger g JOIN pg_class c ON c.oid = g.tgrelid
                             WHERE g.tgname = 'table_written'
                    LOOP
                        EXECUTE format('DROP TRIGGER table_written ON public.%I', t.relname);
                    END LOOP;
                END $$;
                """);
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS watch_table_writes();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS note_table_write();");

            migrationBuilder.DropTable(
                name: "load_step_mark");

            migrationBuilder.DropTable(
                name: "table_fingerprint");

            migrationBuilder.DropTable(
                name: "table_write");
        }
    }
}
