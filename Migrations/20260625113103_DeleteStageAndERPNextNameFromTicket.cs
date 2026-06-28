using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rihla.Migrations
{
    /// <inheritdoc />
    public partial class DeleteStageAndERPNextNameFromTicket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ErpNextTaskName",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "StageName",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "TicketSeq",
                table: "Tickets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ErpNextTaskName",
                table: "Tickets",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StageName",
                table: "Tickets",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TicketSeq",
                table: "Tickets",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
