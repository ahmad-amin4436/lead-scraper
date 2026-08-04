using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadMine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastWhatsAppContactedAt",
                table: "Businesses",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WhatsAppTimesContacted",
                table: "Businesses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "WhatsAppTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_WhatsAppTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppContactLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SenderUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SenderEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ToPhone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TemplateName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppContactLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppContactLogs_WhatsAppTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "WhatsAppTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppContactLogs_BusinessId",
                table: "WhatsAppContactLogs",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppContactLogs_CreatedAt",
                table: "WhatsAppContactLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppContactLogs_SenderUserId_CreatedAt",
                table: "WhatsAppContactLogs",
                columns: new[] { "SenderUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppContactLogs_TemplateId",
                table: "WhatsAppContactLogs",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppTemplates_IsActive",
                table: "WhatsAppTemplates",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppTemplates_Name",
                table: "WhatsAppTemplates",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppContactLogs");

            migrationBuilder.DropTable(
                name: "WhatsAppTemplates");

            migrationBuilder.DropColumn(
                name: "LastWhatsAppContactedAt",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "WhatsAppTimesContacted",
                table: "Businesses");
        }
    }
}
