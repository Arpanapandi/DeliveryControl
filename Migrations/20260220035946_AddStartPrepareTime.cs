using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryControl.Migrations
{
    /// <inheritdoc />
    public partial class AddStartPrepareTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StartPrepareTime",
                table: "DeliverySchedules",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StdPrepareTime",
                table: "DeliverySchedules",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StartPrepareTime",
                table: "Customers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                columns: new[] { "CreatedDate", "StartPrepareTime" },
                values: new object[] { new DateTime(2026, 2, 20, 10, 59, 46, 368, DateTimeKind.Local).AddTicks(7510), 0 });

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                columns: new[] { "CreatedDate", "StartPrepareTime" },
                values: new object[] { new DateTime(2026, 2, 20, 10, 59, 46, 368, DateTimeKind.Local).AddTicks(7512), 0 });

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 20, 10, 59, 46, 368, DateTimeKind.Local).AddTicks(7611));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 20, 10, 59, 46, 368, DateTimeKind.Local).AddTicks(7615));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 20, 10, 59, 46, 368, DateTimeKind.Local).AddTicks(7617));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StartPrepareTime",
                table: "DeliverySchedules");

            migrationBuilder.DropColumn(
                name: "StdPrepareTime",
                table: "DeliverySchedules");

            migrationBuilder.DropColumn(
                name: "StartPrepareTime",
                table: "Customers");

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 19, 15, 25, 14, 22, DateTimeKind.Local).AddTicks(1610));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 19, 15, 25, 14, 22, DateTimeKind.Local).AddTicks(1613));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 19, 15, 25, 14, 22, DateTimeKind.Local).AddTicks(1716));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 19, 15, 25, 14, 22, DateTimeKind.Local).AddTicks(1719));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 19, 15, 25, 14, 22, DateTimeKind.Local).AddTicks(1721));
        }
    }
}
