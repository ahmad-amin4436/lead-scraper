using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadMine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPeopleSearchGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastPeopleSearchedAt",
                table: "Businesses",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastPeopleSearchedAt",
                table: "Businesses");
        }
    }
}
