using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmekliRehberi.Migrations
{
    /// <inheritdoc />
    public partial class AddSocialSecurityRecordDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SocialSecurityRecordDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AppUserId = table.Column<int>(type: "int", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UploadedFileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Days4A = table.Column<int>(type: "int", nullable: true),
                    Days4B = table.Column<int>(type: "int", nullable: true),
                    Days4C = table.Column<int>(type: "int", nullable: true),
                    DaysGM20 = table.Column<int>(type: "int", nullable: true),
                    TotalDays = table.Column<int>(type: "int", nullable: true),
                    Has4A = table.Column<bool>(type: "bit", nullable: false),
                    Has4B = table.Column<bool>(type: "bit", nullable: false),
                    Has4C = table.Column<bool>(type: "bit", nullable: false),
                    FirstRegistrationDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastRegistrationDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ExtractedText = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialSecurityRecordDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocialSecurityRecordDocuments_Users_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SocialSecurityRecordDocuments_AppUserId",
                table: "SocialSecurityRecordDocuments",
                column: "AppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SocialSecurityRecordDocuments");
        }
    }
}
