using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResidentialAmenities.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdminOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Reservations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BuildingId",
                table: "Payments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Backfill for payments created before this column existed: the
            // building of the reservation being paid (no production data yet).
            migrationBuilder.Sql(
                "UPDATE \"Payments\" p SET \"BuildingId\" = r.\"BuildingId\" FROM \"Reservations\" r WHERE r.\"Id\" = p.\"ReservationId\";");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_BuildingId_CreatedAtUtc",
                table: "Payments",
                columns: new[] { "BuildingId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_BuildingId_CreatedAtUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "BuildingId",
                table: "Payments");
        }
    }
}
