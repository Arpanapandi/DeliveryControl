using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DeliveryControl.Migrations
{
    /// <inheritdoc />
    public partial class InitialMigrationReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityLogs",
                columns: table => new
                {
                    LogId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Module = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EntityId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldData = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    NewData = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PerformedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityLogs", x => x.LogId);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    CustomerId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Route = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Cycle = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Docking = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Pickup = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ETD = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Range = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SKID = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Area = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.CustomerId);
                });

            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    ItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Weight = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Volume = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.ItemId);
                });

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Username = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Password = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "DeliverySchedules",
                columns: table => new
                {
                    ScheduleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScheduleNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Route = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Cycle = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    EnterDockTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PickupTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ETD = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Range = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SKID = table.Column<int>(type: "int", nullable: true),
                    Area = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ScheduledDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActualEnterDockTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualStartTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualEndTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PreparationStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DriverStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    VehicleNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DriverName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DriverPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliverySchedules", x => x.ScheduleId);
                    table.ForeignKey(
                        name: "FK_DeliverySchedules_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryItems",
                columns: table => new
                {
                    DeliveryItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScheduleId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ActualQuantity = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TotalWeight = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    TotalVolume = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryItems", x => x.DeliveryItemId);
                    table.ForeignKey(
                        name: "FK_DeliveryItems_DeliverySchedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "DeliverySchedules",
                        principalColumn: "ScheduleId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeliveryItems_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "ItemId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Customers",
                columns: new[] { "CustomerId", "Area", "CreatedDate", "CustomerCode", "CustomerName", "Cycle", "Docking", "ETD", "IsActive", "Pickup", "Range", "Route", "SKID", "UpdatedDate" },
                values: new object[,]
                {
                    { 1, null, new DateTime(2026, 1, 26, 10, 7, 38, 502, DateTimeKind.Local).AddTicks(29), "CUST001", "PT ABC Manufacturing", null, null, null, true, null, null, "Route A", "10 SKID", null },
                    { 2, null, new DateTime(2026, 1, 26, 10, 7, 38, 502, DateTimeKind.Local).AddTicks(33), "CUST002", "PT XYZ Industries", null, null, null, true, null, null, "Route B", "15 SKID", null }
                });

            migrationBuilder.InsertData(
                table: "Items",
                columns: new[] { "ItemId", "Category", "CreatedDate", "Description", "IsActive", "ItemCode", "ItemName", "Unit", "UpdatedDate", "Volume", "Weight" },
                values: new object[,]
                {
                    { 1, "Raw Material", new DateTime(2026, 1, 26, 10, 7, 38, 502, DateTimeKind.Local).AddTicks(488), "Raw material untuk produksi", true, "ITM001", "Raw Material A", "KG", null, null, 1.0m },
                    { 2, "Finished Goods", new DateTime(2026, 1, 26, 10, 7, 38, 502, DateTimeKind.Local).AddTicks(492), "Produk jadi siap kirim", true, "ITM002", "Finished Product B", "PCS", null, null, 2.5m },
                    { 3, "Packaging", new DateTime(2026, 1, 26, 10, 7, 38, 502, DateTimeKind.Local).AddTicks(495), "Material packaging", true, "ITM003", "Packaging Material", "BOX", null, null, 0.5m }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_Module",
                table: "ActivityLogs",
                column: "Module");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_Module_Action",
                table: "ActivityLogs",
                columns: new[] { "Module", "Action" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_Timestamp",
                table: "ActivityLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CustomerCode",
                table: "Customers",
                column: "CustomerCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryItems_ItemId",
                table: "DeliveryItems",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryItems_ScheduleId_ItemId",
                table: "DeliveryItems",
                columns: new[] { "ScheduleId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliverySchedules_CustomerId_ScheduledDate",
                table: "DeliverySchedules",
                columns: new[] { "CustomerId", "ScheduledDate" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliverySchedules_ScheduledDate",
                table: "DeliverySchedules",
                column: "ScheduledDate");

            migrationBuilder.CreateIndex(
                name: "IX_DeliverySchedules_ScheduleNumber",
                table: "DeliverySchedules",
                column: "ScheduleNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliverySchedules_Status",
                table: "DeliverySchedules",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Items_ItemCode",
                table: "Items",
                column: "ItemCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_Key",
                table: "SystemSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityLogs");

            migrationBuilder.DropTable(
                name: "DeliveryItems");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "DeliverySchedules");

            migrationBuilder.DropTable(
                name: "Items");

            migrationBuilder.DropTable(
                name: "Customers");
        }
    }
}
