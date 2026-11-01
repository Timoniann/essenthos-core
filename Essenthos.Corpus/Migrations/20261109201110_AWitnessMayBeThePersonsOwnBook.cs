using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class AWitnessMayBeThePersonsOwnBook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_occupation_witness_kind",
                table: "occupation_witness");

            migrationBuilder.AddCheckConstraint(
                name: "ck_occupation_witness_kind",
                table: "occupation_witness",
                sql: "kind IN ('profession', 'identity', 'own-book')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_occupation_witness_kind",
                table: "occupation_witness");

            migrationBuilder.AddCheckConstraint(
                name: "ck_occupation_witness_kind",
                table: "occupation_witness",
                sql: "kind IN ('profession', 'identity')");
        }
    }
}
