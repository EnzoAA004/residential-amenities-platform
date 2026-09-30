using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResidentialAmenities.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    EndpointHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    P256Dh = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Auth = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationSubscriptions_UserAccounts_UserId",
                        column: x => x.UserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationSubscriptions_UserId",
                table: "NotificationSubscriptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationSubscriptions_UserId_EndpointHash",
                table: "NotificationSubscriptions",
                columns: new[] { "UserId", "EndpointHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationSubscriptions");
        }
    }
}
