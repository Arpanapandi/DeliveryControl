using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryControl.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLabelFromItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Label",
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Label",
                table: "Items",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 29, 10, 8, 50, 350, DateTimeKind.Local).AddTicks(7867));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 29, 10, 8, 50, 350, DateTimeKind.Local).AddTicks(7869));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                columns: new[] { "CreatedDate", "Label" },
                values: new object[] { new DateTime(2026, 1, 29, 10, 8, 50, 350, DateTimeKind.Local).AddTicks(7958), null });

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                columns: new[] { "CreatedDate", "Label" },
                values: new object[] { new DateTime(2026, 1, 29, 10, 8, 50, 350, DateTimeKind.Local).AddTicks(7961), null });

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                columns: new[] { "CreatedDate", "Label" },
                values: new object[] { new DateTime(2026, 1, 29, 10, 8, 50, 350, DateTimeKind.Local).AddTicks(7963), null });
        }
    }
}
