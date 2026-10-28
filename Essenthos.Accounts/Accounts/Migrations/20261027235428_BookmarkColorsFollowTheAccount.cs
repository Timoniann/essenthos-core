using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Accounts.Migrations
{
    /// <inheritdoc />
    public partial class BookmarkColorsFollowTheAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "own_bookmark_colors",
                table: "account",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<List<string>>(
                name: "recent_bookmark_colors",
                table: "account",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "own_bookmark_colors",
                table: "account");

            migrationBuilder.DropColumn(
                name: "recent_bookmark_colors",
                table: "account");
        }
    }
}
