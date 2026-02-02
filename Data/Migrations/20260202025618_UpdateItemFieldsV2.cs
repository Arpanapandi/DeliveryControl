using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryControl.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateItemFieldsV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Customer",
                table: "Items",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ROP",
                table: "Items",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VIN",
                table: "Items",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 2, 9, 56, 18, 5, DateTimeKind.Local).AddTicks(8006));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 2, 9, 56, 18, 5, DateTimeKind.Local).AddTicks(8008));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                columns: new[] { "CreatedDate", "Customer", "ROP", "VIN" },
                values: new object[] { new DateTime(2026, 2, 2, 9, 56, 18, 5, DateTimeKind.Local).AddTicks(8122), null, null, null });

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                columns: new[] { "CreatedDate", "Customer", "ROP", "VIN" },
                values: new object[] { new DateTime(2026, 2, 2, 9, 56, 18, 5, DateTimeKind.Local).AddTicks(8125), null, null, null });

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                columns: new[] { "CreatedDate", "Customer", "ROP", "VIN" },
                values: new object[] { new DateTime(2026, 2, 2, 9, 56, 18, 5, DateTimeKind.Local).AddTicks(8127), null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Customer",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "ROP",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "VIN",
                table: "Items");

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 29, 11, 47, 17, 725, DateTimeKind.Local).AddTicks(336));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 29, 11, 47, 17, 725, DateTimeKind.Local).AddTicks(338));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 29, 11, 47, 17, 725, DateTimeKind.Local).AddTicks(428));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 29, 11, 47, 17, 725, DateTimeKind.Local).AddTicks(431));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 29, 11, 47, 17, 725, DateTimeKind.Local).AddTicks(433));
        }
    }
}
