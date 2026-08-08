using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantApi.Migrations
{
    /// <inheritdoc />
    public partial class AddAuxTableItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aux_table_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    waiter_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_name = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price_at_purchase = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    source_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_order_number = table.Column<int>(type: "integer", nullable: true),
                    source_table_number = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aux_table_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_aux_table_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_aux_table_items_product_id",
                table: "aux_table_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_aux_table_items_restaurant_id",
                table: "aux_table_items",
                column: "restaurant_id");

            migrationBuilder.CreateIndex(
                name: "IX_aux_table_items_restaurant_id_waiter_user_id",
                table: "aux_table_items",
                columns: new[] { "restaurant_id", "waiter_user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aux_table_items");
        }
    }
}
