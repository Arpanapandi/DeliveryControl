using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryControl.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnableCascadeDeleteForItemV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryItems_Items_ItemId",
                table: "DeliveryItems");

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 2, 10, 11, 41, 920, DateTimeKind.Local).AddTicks(5099));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 2, 10, 11, 41, 920, DateTimeKind.Local).AddTicks(5101));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 2, 10, 11, 41, 920, DateTimeKind.Local).AddTicks(5216));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 2, 10, 11, 41, 920, DateTimeKind.Local).AddTicks(5219));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 2, 10, 11, 41, 920, DateTimeKind.Local).AddTicks(5221));

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryItems_Items_ItemId",
                table: "DeliveryItems",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "ItemId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryItems_Items_ItemId",
                table: "DeliveryItems");

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
                column: "CreatedDate",
                value: new DateTime(2026, 2, 2, 9, 56, 18, 5, DateTimeKind.Local).AddTicks(8122));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 2, 9, 56, 18, 5, DateTimeKind.Local).AddTicks(8125));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 2, 9, 56, 18, 5, DateTimeKind.Local).AddTicks(8127));

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryItems_Items_ItemId",
                table: "DeliveryItems",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "ItemId",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
