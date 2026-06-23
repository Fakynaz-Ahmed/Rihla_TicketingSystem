using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rihla.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePassportStatusToEnum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "PassportData",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateOfIssue",
                table: "PassportData",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FullNameAr",
                table: "PassportData",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GenderAr",
                table: "PassportData",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MilitaryStatus",
                table: "PassportData",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NationalId",
                table: "PassportData",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NationalityAr",
                table: "PassportData",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlaceOfBirthAr",
                table: "PassportData",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profession",
                table: "PassportData",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfessionAr",
                table: "PassportData",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "PassportData",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                table: "PassportData");

            migrationBuilder.DropColumn(
                name: "DateOfIssue",
                table: "PassportData");

            migrationBuilder.DropColumn(
                name: "FullNameAr",
                table: "PassportData");

            migrationBuilder.DropColumn(
                name: "GenderAr",
                table: "PassportData");

            migrationBuilder.DropColumn(
                name: "MilitaryStatus",
                table: "PassportData");

            migrationBuilder.DropColumn(
                name: "NationalId",
                table: "PassportData");

            migrationBuilder.DropColumn(
                name: "NationalityAr",
                table: "PassportData");

            migrationBuilder.DropColumn(
                name: "PlaceOfBirthAr",
                table: "PassportData");

            migrationBuilder.DropColumn(
                name: "Profession",
                table: "PassportData");

            migrationBuilder.DropColumn(
                name: "ProfessionAr",
                table: "PassportData");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "PassportData");
        }
    }
}
