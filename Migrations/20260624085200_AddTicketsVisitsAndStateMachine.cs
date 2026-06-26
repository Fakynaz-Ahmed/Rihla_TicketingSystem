using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rihla.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketsVisitsAndStateMachine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EscalatedAt",
                table: "Conversations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EscalationReason",
                table: "Conversations",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpecialistId",
                table: "Conversations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecialistName",
                table: "Conversations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupportId",
                table: "Conversations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupportName",
                table: "Conversations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ErpNextId = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ItemCode = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ListPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ErpNextId = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    ErpNextTaskName = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    CustomerErpNextUserId = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ProductErpNextId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TicketSeq = table.Column<int>(type: "int", nullable: false),
                    StageName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WriteDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ConversationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SpecialistAppUserId = table.Column<int>(type: "int", nullable: true),
                    SupportAppUserId = table.Column<int>(type: "int", nullable: true),
                    VisitId = table.Column<int>(type: "int", nullable: true),
                    TicketRating = table.Column<float>(type: "real", nullable: true),
                    TicketRatingFeedback = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TicketRatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tickets_AppUsers_SpecialistAppUserId",
                        column: x => x.SpecialistAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tickets_AppUsers_SupportAppUserId",
                        column: x => x.SupportAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tickets_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "ConversationId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UserProducts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ErpNextLineName = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    CustomerErpNextUserId = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    ItemCode = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OrderReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OrderDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProducts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserProducts_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Visits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ErpNextId = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    CustomerErpNextUserId = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    AssignedUserId = table.Column<int>(type: "int", nullable: false),
                    AssignedUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PartnerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Stage = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsDone = table.Column<bool>(type: "bit", nullable: false),
                    PlannedStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PlannedEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Deadline = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VisitDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TagsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WriteDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VisitType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MaintenanceType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequestedProductsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsCancelled = table.Column<bool>(type: "bit", nullable: false),
                    CancellationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CloseAttachmentUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    VisitRating = table.Column<float>(type: "real", nullable: true),
                    VisitRatingFeedback = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VisitRatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TicketId = table.Column<int>(type: "int", nullable: true),
                    ProductId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Visits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Visits_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Visits_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "VisitActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VisitId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    User = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StatusFrom = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StatusTo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisitActivities_Visits_VisitId",
                        column: x => x.VisitId,
                        principalTable: "Visits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_SpecialistId",
                table: "Conversations",
                column: "SpecialistId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_SupportId",
                table: "Conversations",
                column: "SupportId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_ErpNextId",
                table: "Products",
                column: "ErpNextId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_ItemCode",
                table: "Products",
                column: "ItemCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_ConversationId",
                table: "Tickets",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_ErpNextId",
                table: "Tickets",
                column: "ErpNextId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_SpecialistAppUserId",
                table: "Tickets",
                column: "SpecialistAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_SupportAppUserId",
                table: "Tickets",
                column: "SupportAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserProducts_CustomerErpNextUserId",
                table: "UserProducts",
                column: "CustomerErpNextUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserProducts_ErpNextLineName",
                table: "UserProducts",
                column: "ErpNextLineName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserProducts_ProductId",
                table: "UserProducts",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitActivities_VisitId",
                table: "VisitActivities",
                column: "VisitId");

            migrationBuilder.CreateIndex(
                name: "IX_Visits_ErpNextId",
                table: "Visits",
                column: "ErpNextId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Visits_ProductId",
                table: "Visits",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Visits_TicketId",
                table: "Visits",
                column: "TicketId");

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_AppUsers_SpecialistId",
                table: "Conversations",
                column: "SpecialistId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_AppUsers_SupportId",
                table: "Conversations",
                column: "SupportId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_AppUsers_SpecialistId",
                table: "Conversations");

            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_AppUsers_SupportId",
                table: "Conversations");

            migrationBuilder.DropTable(
                name: "UserProducts");

            migrationBuilder.DropTable(
                name: "VisitActivities");

            migrationBuilder.DropTable(
                name: "Visits");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_SpecialistId",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_SupportId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "EscalatedAt",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "EscalationReason",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "SpecialistId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "SpecialistName",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "SupportId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "SupportName",
                table: "Conversations");
        }
    }
}
