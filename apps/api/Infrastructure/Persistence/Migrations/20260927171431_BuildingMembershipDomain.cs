using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResidentialAmenities.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BuildingMembershipDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Buildings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Buildings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Floor = table.Column<int>(type: "integer", nullable: false),
                    Door = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Units", x => x.Id);
                    table.UniqueConstraint("AK_Units_BuildingId_Id", x => new { x.BuildingId, x.Id });
                    table.ForeignKey(
                        name: "FK_Units_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResidentMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResidentMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResidentMemberships_Units_BuildingId_UnitId",
                        columns: x => new { x.BuildingId, x.UnitId },
                        principalTable: "Units",
                        principalColumns: new[] { "BuildingId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResidentMemberships_UserAccounts_UserId",
                        column: x => x.UserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResidentMemberships_BuildingId_UnitId_UserId",
                table: "ResidentMemberships",
                columns: new[] { "BuildingId", "UnitId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResidentMemberships_UserId",
                table: "ResidentMemberships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Units_BuildingId_Floor_Door",
                table: "Units",
                columns: new[] { "BuildingId", "Floor", "Door" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Units_BuildingId_Label",
                table: "Units",
                columns: new[] { "BuildingId", "Label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_NormalizedEmail",
                table: "UserAccounts",
                column: "NormalizedEmail",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResidentMemberships");

            migrationBuilder.DropTable(
                name: "Units");

            migrationBuilder.DropTable(
                name: "UserAccounts");

            migrationBuilder.DropTable(
                name: "Buildings");
        }
    }
}
