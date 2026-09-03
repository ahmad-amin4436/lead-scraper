using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadMine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailBounceProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BounceDetectedAt",
                table: "EmailLogs",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BounceDsnCode",
                table: "EmailLogs",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BounceReason",
                table: "EmailLogs",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BounceSmtpCode",
                table: "EmailLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BounceType",
                table: "EmailLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailBounceReason",
                table: "Businesses",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmailBounceStatus",
                table: "Businesses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EmailBounceType",
                table: "Businesses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EmailDsnCode",
                table: "Businesses",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailLastBounceAt",
                table: "Businesses",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmailRetryCount",
                table: "Businesses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EmailSmtpCode",
                table: "Businesses",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailLogs_MessageId",
                table: "EmailLogs",
                column: "MessageId",
                filter: "[MessageId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Businesses_EmailBounceStatus",
                table: "Businesses",
                column: "EmailBounceStatus",
                filter: "[EmailBounceStatus] <> 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailLogs_MessageId",
                table: "EmailLogs");

            migrationBuilder.DropIndex(
                name: "IX_Businesses_EmailBounceStatus",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "BounceDetectedAt",
                table: "EmailLogs");

            migrationBuilder.DropColumn(
                name: "BounceDsnCode",
                table: "EmailLogs");

            migrationBuilder.DropColumn(
                name: "BounceReason",
                table: "EmailLogs");

            migrationBuilder.DropColumn(
                name: "BounceSmtpCode",
                table: "EmailLogs");

            migrationBuilder.DropColumn(
                name: "BounceType",
                table: "EmailLogs");

            migrationBuilder.DropColumn(
                name: "EmailBounceReason",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "EmailBounceStatus",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "EmailBounceType",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "EmailDsnCode",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "EmailLastBounceAt",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "EmailRetryCount",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "EmailSmtpCode",
                table: "Businesses");
        }
    }
}
