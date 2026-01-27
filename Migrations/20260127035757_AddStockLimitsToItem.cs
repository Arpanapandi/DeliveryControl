using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryControl.Migrations
{
    /// <inheritdoc />
    public partial class AddStockLimitsToItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxStock",
                table: "Items",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MinStock",
                table: "Items",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 27, 10, 57, 57, 169, DateTimeKind.Local).AddTicks(9378));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 27, 10, 57, 57, 169, DateTimeKind.Local).AddTicks(9380));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                columns: new[] { "CreatedDate", "MaxStock", "MinStock" },
                values: new object[] { new DateTime(2026, 1, 27, 10, 57, 57, 169, DateTimeKind.Local).AddTicks(9507), 20, 5 });

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                columns: new[] { "CreatedDate", "MaxStock", "MinStock" },
                values: new object[] { new DateTime(2026, 1, 27, 10, 57, 57, 169, DateTimeKind.Local).AddTicks(9509), 20, 5 });

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                columns: new[] { "CreatedDate", "MaxStock", "MinStock" },
                values: new object[] { new DateTime(2026, 1, 27, 10, 57, 57, 169, DateTimeKind.Local).AddTicks(9511), 20, 5 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxStock",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "MinStock",
                table: "Items");

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 27, 8, 26, 14, 933, DateTimeKind.Local).AddTicks(3823));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 27, 8, 26, 14, 933, DateTimeKind.Local).AddTicks(3826));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 27, 8, 26, 14, 933, DateTimeKind.Local).AddTicks(3942));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 27, 8, 26, 14, 933, DateTimeKind.Local).AddTicks(3945));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 27, 8, 26, 14, 933, DateTimeKind.Local).AddTicks(3948));
        }
    }
}
