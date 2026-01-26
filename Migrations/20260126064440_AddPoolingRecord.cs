using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryControl.Migrations
{
    /// <inheritdoc />
    public partial class AddPoolingRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PoolingRecords",
                columns: table => new
                {
                    PoolingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Plant = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Rack = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Column = table.Column<int>(type: "int", nullable: false),
                    Tag = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Kanban = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PoolingRecords", x => x.PoolingId);
                    table.ForeignKey(
                        name: "FK_PoolingRecords_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "ItemId");
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_PoolingRecords_ItemId",
                table: "PoolingRecords",
                column: "ItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PoolingRecords");

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 26, 10, 7, 38, 502, DateTimeKind.Local).AddTicks(29));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 26, 10, 7, 38, 502, DateTimeKind.Local).AddTicks(33));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 26, 10, 7, 38, 502, DateTimeKind.Local).AddTicks(488));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 26, 10, 7, 38, 502, DateTimeKind.Local).AddTicks(492));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 26, 10, 7, 38, 502, DateTimeKind.Local).AddTicks(495));
        }
    }
}
