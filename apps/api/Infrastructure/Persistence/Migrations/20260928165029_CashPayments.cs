using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResidentialAmenities.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RequestedExpirationTime",
                table: "Payments",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "IdempotencyKey",
                table: "Payments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CashConfirmedAtUtc",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CashConfirmedByUserId",
                table: "Payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CashDeclaredAtUtc",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CashConfirmedAtUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CashConfirmedByUserId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CashDeclaredAtUtc",
                table: "Payments");

            migrationBuilder.AlterColumn<string>(
                name: "RequestedExpirationTime",
                table: "Payments",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "IdempotencyKey",
                table: "Payments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);
        }
    }
}
