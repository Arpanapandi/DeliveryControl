using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DeliveryControl.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
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
                    MinStock = table.Column<int>(type: "int", nullable: false),
                    MaxStock = table.Column<int>(type: "int", nullable: false),
                    Plant = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Rack = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    NoRack = table.Column<int>(type: "int", nullable: true),
                    Customer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    VIN = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    QtyLot = table.Column<int>(type: "int", nullable: true),
                    RackMin = table.Column<int>(type: "int", nullable: true),
                    ROP = table.Column<int>(type: "int", nullable: true),
                    RackMax = table.Column<int>(type: "int", nullable: true),
                    CustomerPartNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    KanbanType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
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
                    ScheduleNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Route = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Cycle = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    EnterDockTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PickupTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ETD = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Range = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SKID = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Area = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TotalTargetQuantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalActualQuantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ActualPickupTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ScheduledDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActualEnterDockTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualStartTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualEndTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PreparationStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DriverStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
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
                name: "PullingRecords",
                columns: table => new
                {
                    PullingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Plant = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Rack = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Column = table.Column<int>(type: "int", nullable: false),
                    Tag = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullingRecords", x => x.PullingId);
                    table.ForeignKey(
                        name: "FK_PullingRecords_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "ItemId",
                        onDelete: ReferentialAction.Cascade);
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
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PreparationRecords",
                columns: table => new
                {
                    PreparationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Plant = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Tag = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Kanban = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ScheduleId = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreparationRecords", x => x.PreparationId);
                    table.ForeignKey(
                        name: "FK_PreparationRecords_DeliverySchedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "DeliverySchedules",
                        principalColumn: "ScheduleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Customers",
                columns: new[] { "CustomerId", "Area", "CreatedDate", "CustomerCode", "CustomerName", "Cycle", "Docking", "ETD", "IsActive", "Pickup", "Range", "Route", "SKID", "UpdatedDate" },
                values: new object[,]
                {
                    { 1, null, new DateTime(2026, 2, 12, 10, 57, 6, 13, DateTimeKind.Local).AddTicks(5015), "CUST001", "PT ABC Manufacturing", null, null, null, true, null, null, "Route A", "10 SKID", null },
                    { 2, null, new DateTime(2026, 2, 12, 10, 57, 6, 13, DateTimeKind.Local).AddTicks(5018), "CUST002", "PT XYZ Industries", null, null, null, true, null, null, "Route B", "15 SKID", null }
                });

            migrationBuilder.InsertData(
                table: "Items",
                columns: new[] { "ItemId", "Category", "CreatedDate", "Customer", "CustomerPartNumber", "Description", "IsActive", "ItemCode", "ItemName", "KanbanType", "MaxStock", "MinStock", "NoRack", "Plant", "QtyLot", "ROP", "Rack", "RackMax", "RackMin", "Unit", "UpdatedDate", "VIN", "Volume", "Weight" },
                values: new object[,]
                {
                    { 1, "Raw Material", new DateTime(2026, 2, 12, 10, 57, 6, 13, DateTimeKind.Local).AddTicks(5132), null, null, "Raw material untuk produksi", true, "ITM001", "Raw Material A", null, 20, 5, null, null, null, null, null, null, null, "KG", null, null, null, 1.0m },
                    { 2, "Finished Goods", new DateTime(2026, 2, 12, 10, 57, 6, 13, DateTimeKind.Local).AddTicks(5135), null, null, "Produk jadi siap kirim", true, "ITM002", "Finished Product B", null, 20, 5, null, null, null, null, null, null, null, "PCS", null, null, null, 2.5m },
                    { 3, "Packaging", new DateTime(2026, 2, 12, 10, 57, 6, 13, DateTimeKind.Local).AddTicks(5137), null, null, "Material packaging", true, "ITM003", "Packaging Material", null, 20, 5, null, null, null, null, null, null, null, "BOX", null, null, null, 0.5m }
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
                name: "IX_DeliverySchedules_CreatedDate",
                table: "DeliverySchedules",
                column: "CreatedDate");

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
                unique: true,
                filter: "[ScheduleNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeliverySchedules_Status",
                table: "DeliverySchedules",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Items_CreatedDate",
                table: "Items",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_Items_ItemCode",
                table: "Items",
                column: "ItemCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Items_VIN",
                table: "Items",
                column: "VIN");

            migrationBuilder.CreateIndex(
                name: "IX_PreparationRecords_CreatedDate",
                table: "PreparationRecords",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_PreparationRecords_Label",
                table: "PreparationRecords",
                column: "Label");

            migrationBuilder.CreateIndex(
                name: "IX_PreparationRecords_ScheduleId",
                table: "PreparationRecords",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_PreparationRecords_Tag",
                table: "PreparationRecords",
                column: "Tag");

            migrationBuilder.CreateIndex(
                name: "IX_PreparationRecords_Tag_Label",
                table: "PreparationRecords",
                columns: new[] { "Tag", "Label" });

            migrationBuilder.CreateIndex(
                name: "IX_PullingRecords_CreatedDate",
                table: "PullingRecords",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_PullingRecords_ItemId",
                table: "PullingRecords",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PullingRecords_Label",
                table: "PullingRecords",
                column: "Label");

            migrationBuilder.CreateIndex(
                name: "IX_PullingRecords_Tag",
                table: "PullingRecords",
                column: "Tag");

            migrationBuilder.CreateIndex(
                name: "IX_PullingRecords_Tag_Label",
                table: "PullingRecords",
                columns: new[] { "Tag", "Label" });

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
                name: "PreparationRecords");

            migrationBuilder.DropTable(
                name: "PullingRecords");

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
