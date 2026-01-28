using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryControl.Migrations
{
    /// <inheritdoc />
    public partial class ChangeSkidToString : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "SKID",
                table: "DeliverySchedules",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 14, 2, 28, 437, DateTimeKind.Local).AddTicks(1310));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 14, 2, 28, 437, DateTimeKind.Local).AddTicks(1312));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 14, 2, 28, 437, DateTimeKind.Local).AddTicks(1422));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 14, 2, 28, 437, DateTimeKind.Local).AddTicks(1425));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 14, 2, 28, 437, DateTimeKind.Local).AddTicks(1428));

            migrationBuilder.CreateIndex(
                name: "IX_PreparationRecords_ScheduleId",
                table: "PreparationRecords",
                column: "ScheduleId");

            migrationBuilder.AddForeignKey(
                name: "FK_PreparationRecords_DeliverySchedules_ScheduleId",
                table: "PreparationRecords",
                column: "ScheduleId",
                principalTable: "DeliverySchedules",
                principalColumn: "ScheduleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PreparationRecords_DeliverySchedules_ScheduleId",
                table: "PreparationRecords");

            migrationBuilder.DropIndex(
                name: "IX_PreparationRecords_ScheduleId",
                table: "PreparationRecords");

            migrationBuilder.AlterColumn<int>(
                name: "SKID",
                table: "DeliverySchedules",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

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
    }
}
