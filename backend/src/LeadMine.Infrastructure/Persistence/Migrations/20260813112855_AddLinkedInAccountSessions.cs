using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadMine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkedInAccountSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LinkedInAccountSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageStateJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BudgetDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SearchesToday = table.Column<int>(type: "int", nullable: false),
                    RestrictedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RestrictedReason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkedInAccountSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LinkedInAccountSessions_UserId",
                table: "LinkedInAccountSessions",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LinkedInAccountSessions");
        }
    }
}
