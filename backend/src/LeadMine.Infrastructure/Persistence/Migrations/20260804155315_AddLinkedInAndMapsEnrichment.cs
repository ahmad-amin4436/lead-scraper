using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadMine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkedInAndMapsEnrichment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompanyDescription",
                table: "Businesses",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "EmployeeCount",
                table: "Businesses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrlsJson",
                table: "Businesses",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Industry",
                table: "Businesses",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastLinkedInEnrichedAt",
                table: "Businesses",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastMapsEnrichedAt",
                table: "Businesses",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OpeningHoursJson",
                table: "Businesses",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "PermanentlyClosed",
                table: "Businesses",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlaceId",
                table: "Businesses",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "Businesses",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "People",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    JobTitle = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Headline = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    LinkedInUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ExperienceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EducationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SkillsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsDecisionMaker = table.Column<bool>(type: "bit", nullable: false),
                    DecisionMakerRole = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SearchJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_People", x => x.Id);
                    table.ForeignKey(
                        name: "FK_People_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_People_BusinessId",
                table: "People",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_People_IsDecisionMaker",
                table: "People",
                column: "IsDecisionMaker");

            migrationBuilder.CreateIndex(
                name: "IX_People_LinkedInUrl",
                table: "People",
                column: "LinkedInUrl");

            migrationBuilder.CreateIndex(
                name: "IX_People_OwnerUserId",
                table: "People",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_People_OwnerUserId_CreatedAt",
                table: "People",
                columns: new[] { "OwnerUserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "People");

            migrationBuilder.DropColumn(
                name: "CompanyDescription",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "EmployeeCount",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "ImageUrlsJson",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "Industry",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LastLinkedInEnrichedAt",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LastMapsEnrichedAt",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "OpeningHoursJson",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "PermanentlyClosed",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "PlaceId",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "Businesses");
        }
    }
}
