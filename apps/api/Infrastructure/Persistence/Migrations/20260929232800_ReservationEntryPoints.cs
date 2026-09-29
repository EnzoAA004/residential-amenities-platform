using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResidentialAmenities.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReservationEntryPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReservationEntryPoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    BuildingId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmenityId = table.Column<Guid>(type: "uuid", nullable: false),
                    SuggestedUseType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationEntryPoints", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationEntryPoints_BuildingId_AmenityId",
                table: "ReservationEntryPoints",
                columns: new[] { "BuildingId", "AmenityId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationEntryPoints_Token",
                table: "ReservationEntryPoints",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReservationEntryPoints");
        }
    }
}
