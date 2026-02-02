using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryControl.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnableCascadeDeleteForPoolingRecordV3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PoolingRecords_Items_ItemId",
                table: "PoolingRecords");

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

            migrationBuilder.AddForeignKey(
                name: "FK_PoolingRecords_Items_ItemId",
                table: "PoolingRecords",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "ItemId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PoolingRecords_Items_ItemId",
                table: "PoolingRecords");

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
                name: "FK_PoolingRecords_Items_ItemId",
                table: "PoolingRecords",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "ItemId");
        }
    }
}
