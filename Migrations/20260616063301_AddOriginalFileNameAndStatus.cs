using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmekliRehberi.Migrations
{
    /// <inheritdoc />
    public partial class AddOriginalFileNameAndStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstWorkDate",
                table: "ServiceStatements");

            migrationBuilder.DropColumn(
                name: "TotalPremiumDays",
                table: "ServiceStatements");

            migrationBuilder.RenameColumn(
                name: "LastPeriod",
                table: "ServiceStatements",
                newName: "OriginalFileName");

            migrationBuilder.RenameColumn(
                name: "InsuranceStatus",
                table: "ServiceStatements",
                newName: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Status",
                table: "ServiceStatements",
                newName: "InsuranceStatus");

            migrationBuilder.RenameColumn(
                name: "OriginalFileName",
                table: "ServiceStatements",
                newName: "LastPeriod");

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstWorkDate",
                table: "ServiceStatements",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "TotalPremiumDays",
                table: "ServiceStatements",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
