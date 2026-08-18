using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadMine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailValidationPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EmailConfidence",
                table: "Businesses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailIsCatchAll",
                table: "Businesses",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailIsDisposable",
                table: "Businesses",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailIsRoleAccount",
                table: "Businesses",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailValidatedAt",
                table: "Businesses",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailValidationDetailsJson",
                table: "Businesses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmailBounceChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    MessageId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    BounceReason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailBounceChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailBounceChecks_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Businesses_EmailValidatedAt",
                table: "Businesses",
                column: "EmailValidatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_EmailBounceChecks_BusinessId",
                table: "EmailBounceChecks",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailBounceChecks_Status_SentAt",
                table: "EmailBounceChecks",
                columns: new[] { "Status", "SentAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailBounceChecks");

            migrationBuilder.DropIndex(
                name: "IX_Businesses_EmailValidatedAt",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "EmailConfidence",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "EmailIsCatchAll",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "EmailIsDisposable",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "EmailIsRoleAccount",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "EmailValidatedAt",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "EmailValidationDetailsJson",
                table: "Businesses");
        }
    }
}
