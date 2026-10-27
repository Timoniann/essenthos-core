using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class OccupationsSeparateHeavenlyRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_occupation_category",
                table: "occupation");

            migrationBuilder.AddCheckConstraint(
                name: "ck_occupation_category",
                table: "occupation",
                sql: "category IN ('trade', 'office', 'military_role', 'religious_role', 'heavenly_role')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_occupation_category",
                table: "occupation");

            migrationBuilder.AddCheckConstraint(
                name: "ck_occupation_category",
                table: "occupation",
                sql: "category IN ('trade', 'office', 'military_role', 'religious_role')");
        }
    }
}
