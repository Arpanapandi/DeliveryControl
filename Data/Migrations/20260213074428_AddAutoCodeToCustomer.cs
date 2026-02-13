using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryControl.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoCodeToCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AutoCode",
                table: "Customers",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                columns: new[] { "AutoCode", "CreatedDate" },
                values: new object[] { null, new DateTime(2026, 2, 13, 14, 44, 28, 309, DateTimeKind.Local).AddTicks(4279) });

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                columns: new[] { "AutoCode", "CreatedDate" },
                values: new object[] { null, new DateTime(2026, 2, 13, 14, 44, 28, 309, DateTimeKind.Local).AddTicks(4299) });

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 13, 14, 44, 28, 309, DateTimeKind.Local).AddTicks(4383));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 13, 14, 44, 28, 309, DateTimeKind.Local).AddTicks(4386));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 13, 14, 44, 28, 309, DateTimeKind.Local).AddTicks(4388));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoCode",
                table: "Customers");

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 13, 14, 0, 5, 552, DateTimeKind.Local).AddTicks(3079));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 13, 14, 0, 5, 552, DateTimeKind.Local).AddTicks(3081));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 13, 14, 0, 5, 552, DateTimeKind.Local).AddTicks(3168));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 13, 14, 0, 5, 552, DateTimeKind.Local).AddTicks(3171));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 13, 14, 0, 5, 552, DateTimeKind.Local).AddTicks(3173));
        }
    }
}
