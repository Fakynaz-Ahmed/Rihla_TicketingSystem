using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rihla.Migrations
{
    /// <inheritdoc />
    public partial class AddPassportData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PassportData",
                columns: table => new
                {
                    PassportNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UploadedByUserId = table.Column<int>(type: "int", nullable: false),
                    ConversationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Nationality = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DateOfBirth = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IssuingCountry = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Gender = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    PlaceOfBirth = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RawJsonData = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExtractedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PassportData", x => x.PassportNumber);
                    table.ForeignKey(
                        name: "FK_PassportData_AppUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PassportData_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "ConversationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PassportData_ConversationId",
                table: "PassportData",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_PassportData_UploadedByUserId",
                table: "PassportData",
                column: "UploadedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PassportData");
        }
    }
}
