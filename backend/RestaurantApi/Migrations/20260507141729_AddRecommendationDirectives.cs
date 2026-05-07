using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantApi.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendationDirectives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_order_items_products_product_id",
                table: "order_items");

            migrationBuilder.DropForeignKey(
                name: "FK_similarity_rules_products_product_a_id",
                table: "similarity_rules");

            migrationBuilder.DropForeignKey(
                name: "FK_similarity_rules_products_product_b_id",
                table: "similarity_rules");

            migrationBuilder.CreateTable(
                name: "recommendation_directives",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw_prompt = table.Column<string>(type: "text", nullable: true),
                    parsed_rules = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendation_directives", x => x.id);
                });

            migrationBuilder.AddForeignKey(
                name: "FK_order_items_products_product_id",
                table: "order_items",
                column: "product_id",
                principalTable: "products",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_similarity_rules_products_product_a_id",
                table: "similarity_rules",
                column: "product_a_id",
                principalTable: "products",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_similarity_rules_products_product_b_id",
                table: "similarity_rules",
                column: "product_b_id",
                principalTable: "products",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_order_items_products_product_id",
                table: "order_items");

            migrationBuilder.DropForeignKey(
                name: "FK_similarity_rules_products_product_a_id",
                table: "similarity_rules");

            migrationBuilder.DropForeignKey(
                name: "FK_similarity_rules_products_product_b_id",
                table: "similarity_rules");

            migrationBuilder.DropTable(
                name: "recommendation_directives");

            migrationBuilder.AddForeignKey(
                name: "FK_order_items_products_product_id",
                table: "order_items",
                column: "product_id",
                principalTable: "products",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_similarity_rules_products_product_a_id",
                table: "similarity_rules",
                column: "product_a_id",
                principalTable: "products",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_similarity_rules_products_product_b_id",
                table: "similarity_rules",
                column: "product_b_id",
                principalTable: "products",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
