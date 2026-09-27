using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResidentialAmenities.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeisureReservationsFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    UseType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReservationPriceLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmenityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComponentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    QuotedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationPriceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReservationPriceLines_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReservationResources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmenityId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsExclusive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationResources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReservationResources_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationPriceLines_ReservationId",
                table: "ReservationPriceLines",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReservationResources_AmenityId_ReservationId",
                table: "ReservationResources",
                columns: new[] { "AmenityId", "ReservationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationResources_ReservationId",
                table: "ReservationResources",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_BuildingId_StartsAtUtc_EndsAtUtc",
                table: "Reservations",
                columns: new[] { "BuildingId", "StartsAtUtc", "EndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_Status",
                table: "Reservations",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReservationPriceLines");

            migrationBuilder.DropTable(
                name: "ReservationResources");

            migrationBuilder.DropTable(
                name: "Reservations");
        }
    }
}
