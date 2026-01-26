using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryControl.Migrations
{
    /// <inheritdoc />
    public partial class RefactorPoolingFinal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kanban",
                table: "PoolingRecords");

            migrationBuilder.AlterColumn<string>(
                name: "Tag",
                table: "PoolingRecords",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Label",
                table: "PoolingRecords",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 26, 14, 18, 41, 264, DateTimeKind.Local).AddTicks(1198));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 26, 14, 18, 41, 264, DateTimeKind.Local).AddTicks(1203));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 26, 14, 18, 41, 264, DateTimeKind.Local).AddTicks(1569));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 26, 14, 18, 41, 264, DateTimeKind.Local).AddTicks(1573));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 26, 14, 18, 41, 264, DateTimeKind.Local).AddTicks(1577));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Tag",
                table: "PoolingRecords",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Label",
                table: "PoolingRecords",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<string>(
                name: "Kanban",
                table: "PoolingRecords",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 26, 13, 44, 38, 921, DateTimeKind.Local).AddTicks(3570));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 26, 13, 44, 38, 921, DateTimeKind.Local).AddTicks(3575));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 26, 13, 44, 38, 921, DateTimeKind.Local).AddTicks(4141));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 26, 13, 44, 38, 921, DateTimeKind.Local).AddTicks(4145));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 26, 13, 44, 38, 921, DateTimeKind.Local).AddTicks(4148));
        }
    }
}
