using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadMine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RatingFullPrecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<double>(
                name: "Rating",
                table: "Businesses",
                type: "float",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "float(3)",
                oldPrecision: 3,
                oldScale: 2,
                oldNullable: true);

            // Widening the column does not repair values already written through
            // it: a 4.9 stored as `real` is 4.900000095367432 forever. Provider
            // ratings carry one decimal place, so rounding restores exactly what
            // was scraped rather than inventing precision.
            migrationBuilder.Sql(
                "UPDATE [Businesses] SET [Rating] = ROUND([Rating], 1) WHERE [Rating] IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<double>(
                name: "Rating",
                table: "Businesses",
                type: "float(3)",
                precision: 3,
                scale: 2,
                nullable: true,
                oldClrType: typeof(double),
                oldType: "float",
                oldNullable: true);
        }
    }
}
