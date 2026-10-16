using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueContract.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase11ContractFacilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(
                name: "facility_ids",
                table: "contracts",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            // A single facility becomes a one-element list; null (toàn bệnh viện) becomes the empty list.
            migrationBuilder.Sql("UPDATE contracts SET facility_ids = ARRAY[facility_id] WHERE facility_id IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "facility_id",
                table: "contracts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "facility_id",
                table: "contracts",
                type: "uuid",
                nullable: true);

            // Only a contract of exactly one facility keeps it; several facilities fall back to toàn bệnh viện.
            migrationBuilder.Sql("UPDATE contracts SET facility_id = facility_ids[1] WHERE cardinality(facility_ids) = 1;");

            migrationBuilder.DropColumn(
                name: "facility_ids",
                table: "contracts");
        }
    }
}
