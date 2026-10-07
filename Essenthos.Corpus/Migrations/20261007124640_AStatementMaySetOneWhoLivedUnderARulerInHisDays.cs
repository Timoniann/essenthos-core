using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <summary>
    /// A statement of the kings' timeline may set one who lived under a ruler in his days, dated by his
    /// year: Nehemiah before Artaxerxes, Esther taken to Ahasuerus. Only the check on the role's words
    /// changes, so no row is read or rewritten.
    /// </summary>
    public partial class AStatementMaySetOneWhoLivedUnderARulerInHisDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_reign_statement_role",
                table: "reign_statement");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reign_statement_role",
                table: "reign_statement",
                sql: "role IN ('prophet', 'nation', 'accession', 'subject')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_reign_statement_role",
                table: "reign_statement");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reign_statement_role",
                table: "reign_statement",
                sql: "role IN ('prophet', 'nation', 'accession')");
        }
    }
}
