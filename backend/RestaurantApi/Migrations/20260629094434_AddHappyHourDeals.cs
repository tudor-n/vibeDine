using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantApi.Migrations
{
    /// <inheritdoc />
    public partial class AddHappyHourDeals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_directives");

            migrationBuilder.CreateTable(
                name: "happy_hour_deals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    discount_percent = table.Column<int>(type: "integer", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_happy_hour_deals", x => x.id);
                    table.ForeignKey(
                        name: "FK_happy_hour_deals_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_happy_hour_deals_product_id",
                table: "happy_hour_deals",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_happy_hour_deals_restaurant_id",
                table: "happy_hour_deals",
                column: "restaurant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "happy_hour_deals");

            migrationBuilder.CreateTable(
                name: "ai_directives",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: true),
                    parsed_rules = table.Column<string>(type: "jsonb", nullable: true),
                    raw_prompt = table.Column<string>(type: "text", nullable: true),
                    restaurant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_directives", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_directives_restaurant_id",
                table: "ai_directives",
                column: "restaurant_id");
        }
    }
}
