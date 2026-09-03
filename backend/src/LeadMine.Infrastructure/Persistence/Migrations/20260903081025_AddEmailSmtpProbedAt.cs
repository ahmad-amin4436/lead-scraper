using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadMine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailSmtpProbedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailSmtpProbedAt",
                table: "Businesses",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Businesses_EmailSmtpProbedAt",
                table: "Businesses",
                column: "EmailSmtpProbedAt",
                filter: "[EmailSmtpProbedAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Businesses_EmailSmtpProbedAt",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "EmailSmtpProbedAt",
                table: "Businesses");
        }
    }
}
