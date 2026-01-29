using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryControl.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddItemMappingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FgMappings");

            migrationBuilder.AddColumn<int>(
                name: "NoRack",
                table: "Items",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Plant",
                table: "Items",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QtyLot",
                table: "Items",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Rack",
                table: "Items",
                type: "TEXT",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RackMax",
                table: "Items",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RackMin",
                table: "Items",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 29, 9, 44, 5, 227, DateTimeKind.Local).AddTicks(9693));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 29, 9, 44, 5, 227, DateTimeKind.Local).AddTicks(9695));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                columns: new[] { "CreatedDate", "NoRack", "Plant", "QtyLot", "Rack", "RackMax", "RackMin" },
                values: new object[] { new DateTime(2026, 1, 29, 9, 44, 5, 227, DateTimeKind.Local).AddTicks(9781), null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                columns: new[] { "CreatedDate", "NoRack", "Plant", "QtyLot", "Rack", "RackMax", "RackMin" },
                values: new object[] { new DateTime(2026, 1, 29, 9, 44, 5, 227, DateTimeKind.Local).AddTicks(9783), null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                columns: new[] { "CreatedDate", "NoRack", "Plant", "QtyLot", "Rack", "RackMax", "RackMin" },
                values: new object[] { new DateTime(2026, 1, 29, 9, 44, 5, 227, DateTimeKind.Local).AddTicks(9785), null, null, null, null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NoRack",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Plant",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "QtyLot",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Rack",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "RackMax",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "RackMin",
                table: "Items");

            migrationBuilder.CreateTable(
                name: "FgMappings",
                columns: table => new
                {
                    FgMappingId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MaxStock = table.Column<int>(type: "INTEGER", nullable: false),
                    MinStock = table.Column<int>(type: "INTEGER", nullable: false),
                    NoRack = table.Column<int>(type: "INTEGER", nullable: false),
                    Plant = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    QtyLot = table.Column<int>(type: "INTEGER", nullable: false),
                    Rack = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FgMappings", x => x.FgMappingId);
                    table.ForeignKey(
                        name: "FK_FgMappings_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "ItemId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 16, 30, 58, 775, DateTimeKind.Local).AddTicks(4569));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 16, 30, 58, 775, DateTimeKind.Local).AddTicks(4572));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 16, 30, 58, 775, DateTimeKind.Local).AddTicks(4679));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 16, 30, 58, 775, DateTimeKind.Local).AddTicks(4682));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 1, 28, 16, 30, 58, 775, DateTimeKind.Local).AddTicks(4684));

            migrationBuilder.CreateIndex(
                name: "IX_FgMappings_ItemId",
                table: "FgMappings",
                column: "ItemId",
                unique: true);
        }
    }
}
