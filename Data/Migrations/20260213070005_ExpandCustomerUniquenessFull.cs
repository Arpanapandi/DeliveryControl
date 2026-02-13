using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryControl.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExpandCustomerUniquenessFull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_CustomerCode_CustomerName",
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

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CustomerCode_CustomerName_Route_Cycle_Docking_Pickup_ETD_Range_SKID_Area",
                table: "Customers",
                columns: new[] { "CustomerCode", "CustomerName", "Route", "Cycle", "Docking", "Pickup", "ETD", "Range", "SKID", "Area" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_CustomerCode_CustomerName_Route_Cycle_Docking_Pickup_ETD_Range_SKID_Area",
                table: "Customers");

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 13, 13, 3, 24, 183, DateTimeKind.Local).AddTicks(1977));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 13, 13, 3, 24, 183, DateTimeKind.Local).AddTicks(1979));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 13, 13, 3, 24, 183, DateTimeKind.Local).AddTicks(2076));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 13, 13, 3, 24, 183, DateTimeKind.Local).AddTicks(2079));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 13, 13, 3, 24, 183, DateTimeKind.Local).AddTicks(2082));

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CustomerCode_CustomerName",
                table: "Customers",
                columns: new[] { "CustomerCode", "CustomerName" },
                unique: true);
        }
    }
}
