using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTableZones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "position_x",
                table: "tables",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "position_y",
                table: "tables",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<Guid>(
                name: "zone_id",
                table: "tables",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "table_zones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_table_zones", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tables_zone_id",
                table: "tables",
                column: "zone_id");

            migrationBuilder.CreateIndex(
                name: "IX_table_zones_restaurant_id_sort_order",
                table: "table_zones",
                columns: new[] { "restaurant_id", "sort_order" });

            migrationBuilder.AddForeignKey(
                name: "FK_tables_table_zones_zone_id",
                table: "tables",
                column: "zone_id",
                principalTable: "table_zones",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tables_table_zones_zone_id",
                table: "tables");

            migrationBuilder.DropTable(
                name: "table_zones");

            migrationBuilder.DropIndex(
                name: "IX_tables_zone_id",
                table: "tables");

            migrationBuilder.DropColumn(
                name: "position_x",
                table: "tables");

            migrationBuilder.DropColumn(
                name: "position_y",
                table: "tables");

            migrationBuilder.DropColumn(
                name: "zone_id",
                table: "tables");
        }
    }
}
