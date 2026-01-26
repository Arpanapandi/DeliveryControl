using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryControl.Migrations
{
    /// <inheritdoc />
    public partial class AddPreparationRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PreparationRecords",
                columns: table => new
                {
                    PreparationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Plant = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Tag = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Kanban = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreparationRecords", x => x.PreparationId);
                });

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 26, 14, 49, 37, 168, DateTimeKind.Local).AddTicks(8507));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 26, 14, 49, 37, 168, DateTimeKind.Local).AddTicks(8512));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 26, 14, 49, 37, 168, DateTimeKind.Local).AddTicks(9046));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 26, 14, 49, 37, 168, DateTimeKind.Local).AddTicks(9050));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 26, 14, 49, 37, 168, DateTimeKind.Local).AddTicks(9054));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PreparationRecords");

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
    }
}
