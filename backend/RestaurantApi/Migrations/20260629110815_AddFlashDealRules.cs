using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantApi.Migrations
{
    /// <inheritdoc />
    public partial class AddFlashDealRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "flash_deal_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    discount_percent = table.Column<int>(type: "integer", nullable: false),
                    min_order_total = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flash_deal_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_flash_deal_rules_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_flash_deal_rules_product_id",
                table: "flash_deal_rules",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_flash_deal_rules_restaurant_id",
                table: "flash_deal_rules",
                column: "restaurant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "flash_deal_rules");
        }
    }
}
