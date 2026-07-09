using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantApi.Migrations
{
    /// <inheritdoc />
    public partial class AddIngredientStockTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "stock_tracking_mode",
                table: "products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "stock_decremented",
                table: "orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ingredients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    unit = table.Column<int>(type: "integer", nullable: false),
                    quantity_on_hand = table.Column<decimal>(type: "numeric(12,3)", nullable: false),
                    low_stock_threshold = table.Column<decimal>(type: "numeric(12,3)", nullable: false, defaultValue: 0m),
                    linked_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ingredients", x => x.id);
                    table.ForeignKey(
                        name: "FK_ingredients_products_linked_product_id",
                        column: x => x.linked_product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recipe_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity_per_portion = table.Column<decimal>(type: "numeric(12,3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recipe_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_recipe_items_ingredients_ingredient_id",
                        column: x => x.ingredient_id,
                        principalTable: "ingredients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recipe_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ingredients_linked_product_id",
                table: "ingredients",
                column: "linked_product_id",
                unique: true,
                filter: "linked_product_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ingredients_restaurant_id",
                table: "ingredients",
                column: "restaurant_id");

            migrationBuilder.CreateIndex(
                name: "IX_recipe_items_ingredient_id",
                table: "recipe_items",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "IX_recipe_items_product_id_ingredient_id",
                table: "recipe_items",
                columns: new[] { "product_id", "ingredient_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recipe_items_restaurant_id",
                table: "recipe_items",
                column: "restaurant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recipe_items");

            migrationBuilder.DropTable(
                name: "ingredients");

            migrationBuilder.DropColumn(
                name: "stock_tracking_mode",
                table: "products");

            migrationBuilder.DropColumn(
                name: "stock_decremented",
                table: "orders");
        }
    }
}
