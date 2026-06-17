using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmekliRehberi.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceStatementsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServiceStatements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AppUserId = table.Column<int>(type: "int", nullable: false),
                    FirstWorkDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalPremiumDays = table.Column<int>(type: "int", nullable: false),
                    InsuranceStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastPeriod = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UploadedFileName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceStatements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceStatements_Users_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceStatements_AppUserId",
                table: "ServiceStatements",
                column: "AppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceStatements");
        }
    }
}
