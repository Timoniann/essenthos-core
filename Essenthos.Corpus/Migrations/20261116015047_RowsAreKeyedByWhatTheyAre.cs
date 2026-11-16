using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class RowsAreKeyedByWhatTheyAre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No build writes claims beside a name on a word; a claim's key becomes the word and the record it names.
            migrationBuilder.Sql("TRUNCATE word_entity_claim");

            migrationBuilder.DropForeignKey(
                name: "fk_word_entity_claim_word_entity_word_entity_id",
                table: "word_entity_claim");

            migrationBuilder.DropIndex(
                name: "ix_word_entity_claim_word_entity_id_method_source",
                table: "word_entity_claim");

            migrationBuilder.DropPrimaryKey(
                name: "pk_word_entity",
                table: "word_entity");

            migrationBuilder.DropIndex(
                name: "ix_word_entity_word_id_entity_id",
                table: "word_entity");

            migrationBuilder.DropPrimaryKey(
                name: "pk_strong_rendering",
                table: "strong_rendering");

            migrationBuilder.DropIndex(
                name: "ix_strong_rendering_text_id_strong_number_rank",
                table: "strong_rendering");

            migrationBuilder.DropColumn(
                name: "id",
                table: "word_entity");

            migrationBuilder.DropColumn(
                name: "id",
                table: "strong_rendering");

            migrationBuilder.RenameColumn(
                name: "word_entity_id",
                table: "word_entity_claim",
                newName: "word_id");

            migrationBuilder.AddColumn<int>(
                name: "entity_id",
                table: "word_entity_claim",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<long>(
                name: "id",
                table: "verse_reference",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddPrimaryKey(
                name: "pk_word_entity",
                table: "word_entity",
                columns: new[] { "word_id", "entity_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_strong_rendering",
                table: "strong_rendering",
                columns: new[] { "text_id", "strong_number", "rank" });

            migrationBuilder.CreateIndex(
                name: "ix_word_entity_claim_word_id_entity_id_method_source",
                table: "word_entity_claim",
                columns: new[] { "word_id", "entity_id", "method", "source" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_word_entity_claim_word_entity_word_id_entity_id",
                table: "word_entity_claim",
                columns: new[] { "word_id", "entity_id" },
                principalTable: "word_entity",
                principalColumns: new[] { "word_id", "entity_id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_word_entity_claim_word_entity_word_id_entity_id",
                table: "word_entity_claim");

            migrationBuilder.DropIndex(
                name: "ix_word_entity_claim_word_id_entity_id_method_source",
                table: "word_entity_claim");

            migrationBuilder.DropPrimaryKey(
                name: "pk_word_entity",
                table: "word_entity");

            migrationBuilder.DropPrimaryKey(
                name: "pk_strong_rendering",
                table: "strong_rendering");

            migrationBuilder.DropColumn(
                name: "entity_id",
                table: "word_entity_claim");

            migrationBuilder.RenameColumn(
                name: "word_id",
                table: "word_entity_claim",
                newName: "word_entity_id");

            migrationBuilder.AddColumn<long>(
                name: "id",
                table: "word_entity",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<int>(
                name: "id",
                table: "verse_reference",
                type: "integer",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<int>(
                name: "id",
                table: "strong_rendering",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddPrimaryKey(
                name: "pk_word_entity",
                table: "word_entity",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_strong_rendering",
                table: "strong_rendering",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "ix_word_entity_claim_word_entity_id_method_source",
                table: "word_entity_claim",
                columns: new[] { "word_entity_id", "method", "source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_word_entity_word_id_entity_id",
                table: "word_entity",
                columns: new[] { "word_id", "entity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_strong_rendering_text_id_strong_number_rank",
                table: "strong_rendering",
                columns: new[] { "text_id", "strong_number", "rank" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_word_entity_claim_word_entity_word_entity_id",
                table: "word_entity_claim",
                column: "word_entity_id",
                principalTable: "word_entity",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
