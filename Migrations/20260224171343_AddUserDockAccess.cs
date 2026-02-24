using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryControl.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDockAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DockId",
                table: "DeliverySchedules",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Docks",
                columns: table => new
                {
                    DockId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: false),
                    DockCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DockName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Docks", x => x.DockId);
                    table.ForeignKey(
                        name: "FK_Docks_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserDockAccesses",
                columns: table => new
                {
                    UserDockAccessId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    DockId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDockAccesses", x => x.UserDockAccessId);
                    table.ForeignKey(
                        name: "FK_UserDockAccesses_Docks_DockId",
                        column: x => x.DockId,
                        principalTable: "Docks",
                        principalColumn: "DockId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserDockAccesses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 25, 0, 13, 42, 601, DateTimeKind.Local).AddTicks(4042));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 25, 0, 13, 42, 601, DateTimeKind.Local).AddTicks(4045));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 25, 0, 13, 42, 601, DateTimeKind.Local).AddTicks(4324));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 25, 0, 13, 42, 601, DateTimeKind.Local).AddTicks(4328));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 25, 0, 13, 42, 601, DateTimeKind.Local).AddTicks(4331));

            migrationBuilder.CreateIndex(
                name: "IX_DeliverySchedules_DockId",
                table: "DeliverySchedules",
                column: "DockId");

            migrationBuilder.CreateIndex(
                name: "IX_Docks_CustomerId",
                table: "Docks",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_UserDockAccesses_DockId",
                table: "UserDockAccesses",
                column: "DockId");

            migrationBuilder.CreateIndex(
                name: "IX_UserDockAccesses_UserId_DockId",
                table: "UserDockAccesses",
                columns: new[] { "UserId", "DockId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliverySchedules_Docks_DockId",
                table: "DeliverySchedules",
                column: "DockId",
                principalTable: "Docks",
                principalColumn: "DockId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeliverySchedules_Docks_DockId",
                table: "DeliverySchedules");

            migrationBuilder.DropTable(
                name: "UserDockAccesses");

            migrationBuilder.DropTable(
                name: "Docks");

            migrationBuilder.DropIndex(
                name: "IX_DeliverySchedules_DockId",
                table: "DeliverySchedules");

            migrationBuilder.DropColumn(
                name: "DockId",
                table: "DeliverySchedules");

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 21, 11, 24, 44, 729, DateTimeKind.Local).AddTicks(327));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 21, 11, 24, 44, 729, DateTimeKind.Local).AddTicks(330));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 21, 11, 24, 44, 729, DateTimeKind.Local).AddTicks(662));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 21, 11, 24, 44, 729, DateTimeKind.Local).AddTicks(666));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 2, 21, 11, 24, 44, 729, DateTimeKind.Local).AddTicks(669));
        }
    }
}
