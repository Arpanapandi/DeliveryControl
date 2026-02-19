using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryControl.Migrations
{
    /// <inheritdoc />
    public partial class AddPrepareFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ActPrepareTime",
                table: "DeliverySchedules",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadyToDockTime",
                table: "DeliverySchedules",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StdPrepareTime",
                table: "Customers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                columns: new[] { "CreatedDate", "StdPrepareTime" },
                values: new object[] { new DateTime(2026, 2, 19, 15, 25, 14, 22, DateTimeKind.Local).AddTicks(1610), 0 });

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                columns: new[] { "CreatedDate", "StdPrepareTime" },
                values: new object[] { new DateTime(2026, 2, 19, 15, 25, 14, 22, DateTimeKind.Local).AddTicks(1613), 0 });

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActPrepareTime",
                table: "DeliverySchedules");

            migrationBuilder.DropColumn(
                name: "ReadyToDockTime",
                table: "DeliverySchedules");

            migrationBuilder.DropColumn(
                name: "StdPrepareTime",
                table: "Customers");

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 16, 15, 25, 43, 382, DateTimeKind.Local).AddTicks(3474));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 16, 15, 25, 43, 382, DateTimeKind.Local).AddTicks(3476));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 16, 15, 25, 43, 382, DateTimeKind.Local).AddTicks(3580));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 16, 15, 25, 43, 382, DateTimeKind.Local).AddTicks(3583));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 16, 15, 25, 43, 382, DateTimeKind.Local).AddTicks(3586));
        }
    }
}
