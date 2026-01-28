using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryControl.Migrations
{
    /// <inheritdoc />
    public partial class AddPreparationIntegrationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ScheduleId",
                table: "PreparationRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActualPickupTime",
                table: "DeliverySchedules",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalActualQuantity",
                table: "DeliverySchedules",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalTargetQuantity",
                table: "DeliverySchedules",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 10, 1, 54, 137, DateTimeKind.Local).AddTicks(301));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 10, 1, 54, 137, DateTimeKind.Local).AddTicks(303));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 10, 1, 54, 137, DateTimeKind.Local).AddTicks(410));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 10, 1, 54, 137, DateTimeKind.Local).AddTicks(412));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 10, 1, 54, 137, DateTimeKind.Local).AddTicks(415));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScheduleId",
                table: "PreparationRecords");

            migrationBuilder.DropColumn(
                name: "ActualPickupTime",
                table: "DeliverySchedules");

            migrationBuilder.DropColumn(
                name: "TotalActualQuantity",
                table: "DeliverySchedules");

            migrationBuilder.DropColumn(
                name: "TotalTargetQuantity",
                table: "DeliverySchedules");

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 27, 14, 4, 24, 637, DateTimeKind.Local).AddTicks(1912));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 27, 14, 4, 24, 637, DateTimeKind.Local).AddTicks(1914));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 27, 14, 4, 24, 637, DateTimeKind.Local).AddTicks(2013));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 27, 14, 4, 24, 637, DateTimeKind.Local).AddTicks(2016));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 27, 14, 4, 24, 637, DateTimeKind.Local).AddTicks(2018));
        }
    }
}
