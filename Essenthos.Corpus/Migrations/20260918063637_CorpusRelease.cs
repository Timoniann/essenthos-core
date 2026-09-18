using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class CorpusRelease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "corpus_release",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    built_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    manifest_sha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    migration_head = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    forge_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    original_words = table.Column<int>(type: "integer", nullable: false),
                    translations = table.Column<int>(type: "integer", nullable: false),
                    strong_entries = table.Column<int>(type: "integer", nullable: false),
                    people = table.Column<int>(type: "integer", nullable: false),
                    places = table.Column<int>(type: "integer", nullable: false),
                    peoples = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_corpus_release", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "corpus_release");
        }
    }
}
