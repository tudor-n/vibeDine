using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantApi.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiPersonOrdering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_table_id",
                table: "orders");

            migrationBuilder.AddColumn<Guid>(
                name: "guest_id",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "merged_into_order_id",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "table_guests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_table_guests", x => x.id);
                    table.ForeignKey(
                        name: "FK_table_guests_tables_table_id",
                        column: x => x.table_id,
                        principalTable: "tables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_orders_table_id_guest_id_status",
                table: "orders",
                columns: new[] { "table_id", "guest_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_table_guests_restaurant_id",
                table: "table_guests",
                column: "restaurant_id");

            migrationBuilder.CreateIndex(
                name: "IX_table_guests_table_id",
                table: "table_guests",
                column: "table_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "table_guests");

            migrationBuilder.DropIndex(
                name: "IX_orders_table_id_guest_id_status",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "guest_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "merged_into_order_id",
                table: "orders");

            migrationBuilder.CreateIndex(
                name: "IX_orders_table_id",
                table: "orders",
                column: "table_id");
        }
    }
}
