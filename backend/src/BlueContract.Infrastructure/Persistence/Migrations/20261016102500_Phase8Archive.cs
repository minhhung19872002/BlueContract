using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueContract.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase8Archive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archive_completed_at",
                table: "contracts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "archive_completed_by_name",
                table: "contracts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_archive_complete",
                table: "contracts",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "archive_completed_at",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "archive_completed_by_name",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "is_archive_complete",
                table: "contracts");
        }
    }
}
