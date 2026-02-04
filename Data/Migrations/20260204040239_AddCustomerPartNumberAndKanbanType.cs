using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryControl.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerPartNumberAndKanbanType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomerPartNumber",
                table: "Items",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KanbanType",
                table: "Items",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 4, 11, 2, 38, 788, DateTimeKind.Local).AddTicks(4646));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 4, 11, 2, 38, 788, DateTimeKind.Local).AddTicks(4648));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                columns: new[] { "CreatedDate", "CustomerPartNumber", "KanbanType" },
                values: new object[] { new DateTime(2026, 2, 4, 11, 2, 38, 788, DateTimeKind.Local).AddTicks(4740), null, null });

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                columns: new[] { "CreatedDate", "CustomerPartNumber", "KanbanType" },
                values: new object[] { new DateTime(2026, 2, 4, 11, 2, 38, 788, DateTimeKind.Local).AddTicks(4743), null, null });

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                columns: new[] { "CreatedDate", "CustomerPartNumber", "KanbanType" },
                values: new object[] { new DateTime(2026, 2, 4, 11, 2, 38, 788, DateTimeKind.Local).AddTicks(4745), null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomerPartNumber",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "KanbanType",
                table: "Items");

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 2, 10, 14, 37, 435, DateTimeKind.Local).AddTicks(8343));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 2, 10, 14, 37, 435, DateTimeKind.Local).AddTicks(8346));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 2, 10, 14, 37, 435, DateTimeKind.Local).AddTicks(8487));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 2, 10, 14, 37, 435, DateTimeKind.Local).AddTicks(8490));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 2, 10, 14, 37, 435, DateTimeKind.Local).AddTicks(8492));
        }
    }
}
