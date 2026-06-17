using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmekliRehberi.Migrations
{
    /// <inheritdoc />
    public partial class AddPremiumTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PremiumDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AppUserId = table.Column<int>(type: "int", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UploadedFileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TotalPremiumDays = table.Column<int>(type: "int", nullable: true),
                    LastPeriod = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HasMissingDays = table.Column<bool>(type: "bit", nullable: false),
                    ExtractedText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PremiumDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PremiumDocuments_Users_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PremiumRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PremiumDocumentId = table.Column<int>(type: "int", nullable: false),
                    InsuranceBranch = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InsuranceStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Period = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DocumentType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Days = table.Column<int>(type: "int", nullable: false),
                    PekAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    EntryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExitDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MissingDayReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExitReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsYearTotal = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PremiumRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PremiumRecords_PremiumDocuments_PremiumDocumentId",
                        column: x => x.PremiumDocumentId,
                        principalTable: "PremiumDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PremiumDocuments_AppUserId",
                table: "PremiumDocuments",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PremiumRecords_PremiumDocumentId",
                table: "PremiumRecords",
                column: "PremiumDocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PremiumRecords");

            migrationBuilder.DropTable(
                name: "PremiumDocuments");
        }
    }
}
