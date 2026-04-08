using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantApi.Migrations
{
    /// <inheritdoc />
    public partial class AddSimilarityRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "similarity_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_a_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_b_id = table.Column<Guid>(type: "uuid", nullable: false),
                    score = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    rule_type = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_similarity_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_similarity_rules_products_product_a_id",
                        column: x => x.product_a_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_similarity_rules_products_product_b_id",
                        column: x => x.product_b_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_similarity_rules_product_a_id",
                table: "similarity_rules",
                column: "product_a_id");

            migrationBuilder.CreateIndex(
                name: "IX_similarity_rules_product_b_id",
                table: "similarity_rules",
                column: "product_b_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "similarity_rules");
        }
    }
}
