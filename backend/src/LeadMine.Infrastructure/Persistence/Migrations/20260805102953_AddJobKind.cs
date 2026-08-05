using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadMine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "SearchJobs",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "SearchJobs");
        }
    }
}
