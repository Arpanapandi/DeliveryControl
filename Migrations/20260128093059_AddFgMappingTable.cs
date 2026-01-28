using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryControl.Migrations
{
    /// <inheritdoc />
    public partial class AddFgMappingTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PreparationRecords_DeliverySchedules_ScheduleId",
                table: "PreparationRecords");

            migrationBuilder.CreateTable(
                name: "FgMappings",
                columns: table => new
                {
                    FgMappingId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Plant = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Rack = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    NoRack = table.Column<int>(type: "INTEGER", nullable: false),
                    QtyLot = table.Column<int>(type: "INTEGER", nullable: false),
                    MinStock = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxStock = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FgMappings", x => x.FgMappingId);
                    table.ForeignKey(
                        name: "FK_FgMappings_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "ItemId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 16, 30, 58, 775, DateTimeKind.Local).AddTicks(4569));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 16, 30, 58, 775, DateTimeKind.Local).AddTicks(4572));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 16, 30, 58, 775, DateTimeKind.Local).AddTicks(4679));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 16, 30, 58, 775, DateTimeKind.Local).AddTicks(4682));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 16, 30, 58, 775, DateTimeKind.Local).AddTicks(4684));

            migrationBuilder.CreateIndex(
                name: "IX_FgMappings_ItemId",
                table: "FgMappings",
                column: "ItemId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PreparationRecords_DeliverySchedules_ScheduleId",
                table: "PreparationRecords",
                column: "ScheduleId",
                principalTable: "DeliverySchedules",
                principalColumn: "ScheduleId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PreparationRecords_DeliverySchedules_ScheduleId",
                table: "PreparationRecords");

            migrationBuilder.DropTable(
                name: "FgMappings");

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 14, 2, 28, 437, DateTimeKind.Local).AddTicks(1310));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 14, 2, 28, 437, DateTimeKind.Local).AddTicks(1312));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 14, 2, 28, 437, DateTimeKind.Local).AddTicks(1422));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 14, 2, 28, 437, DateTimeKind.Local).AddTicks(1425));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 14, 2, 28, 437, DateTimeKind.Local).AddTicks(1428));

            migrationBuilder.AddForeignKey(
                name: "FK_PreparationRecords_DeliverySchedules_ScheduleId",
                table: "PreparationRecords",
                column: "ScheduleId",
                principalTable: "DeliverySchedules",
                principalColumn: "ScheduleId");
        }
    }
}
