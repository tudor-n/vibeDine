using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantApi.Migrations
{
    /// <inheritdoc />
    public partial class AddProductSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "sort_order",
                table: "products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Backfill: per-category, assign 0..n-1 in alphabetical order so the
            // initial display matches today's behaviour. New products get
            // (max + 1) within their category on insert.
            migrationBuilder.Sql(@"
                WITH ordered AS (
                    SELECT id,
                           ROW_NUMBER() OVER (PARTITION BY category_id ORDER BY name) - 1 AS rn
                    FROM products
                )
                UPDATE products p
                SET sort_order = ordered.rn
                FROM ordered
                WHERE p.id = ordered.id;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sort_order",
                table: "products");
        }
    }
}
