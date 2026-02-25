using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryControl.Migrations
{
    /// <inheritdoc />
    public partial class AddStockSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SnapshotDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SnapshotTime = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    ItemCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ItemName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Plant = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    StockQty = table.Column<int>(type: "INTEGER", nullable: false),
                    DaysCoverage = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    StockLevel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsManual = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockSnapshots", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 25, 7, 47, 34, 595, DateTimeKind.Local).AddTicks(1814));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 25, 7, 47, 34, 595, DateTimeKind.Local).AddTicks(1819));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 25, 7, 47, 34, 595, DateTimeKind.Local).AddTicks(2022));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 25, 7, 47, 34, 595, DateTimeKind.Local).AddTicks(2027));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 25, 7, 47, 34, 595, DateTimeKind.Local).AddTicks(2030));

            migrationBuilder.CreateIndex(
                name: "IX_StockSnapshots_SnapshotDate_Plant_ItemCode",
                table: "StockSnapshots",
                columns: new[] { "SnapshotDate", "Plant", "ItemCode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockSnapshots");

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 24, 23, 51, 21, 810, DateTimeKind.Local).AddTicks(865));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 24, 23, 51, 21, 810, DateTimeKind.Local).AddTicks(868));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 24, 23, 51, 21, 810, DateTimeKind.Local).AddTicks(999));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 24, 23, 51, 21, 810, DateTimeKind.Local).AddTicks(1002));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 24, 23, 51, 21, 810, DateTimeKind.Local).AddTicks(1005));
        }
    }
}
