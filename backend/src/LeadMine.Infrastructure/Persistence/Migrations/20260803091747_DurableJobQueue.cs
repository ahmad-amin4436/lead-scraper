using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadMine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DurableJobQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "SearchJobs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CompletedTaskKeysJson",
                table: "SearchJobs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAt",
                table: "SearchJobs",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseOwner",
                table: "SearchJobs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecentResultsJson",
                table: "SearchJobs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "SearchJobs");

            migrationBuilder.DropColumn(
                name: "CompletedTaskKeysJson",
                table: "SearchJobs");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "SearchJobs");

            migrationBuilder.DropColumn(
                name: "LeaseOwner",
                table: "SearchJobs");

            migrationBuilder.DropColumn(
                name: "RecentResultsJson",
                table: "SearchJobs");
        }
    }
}
