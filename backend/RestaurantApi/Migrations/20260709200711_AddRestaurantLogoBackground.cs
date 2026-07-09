using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantApi.Migrations
{
    /// <inheritdoc />
    public partial class AddRestaurantLogoBackground : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "logo_background",
                table: "restaurants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "logo_is_dark",
                table: "restaurants",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "logo_background",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "logo_is_dark",
                table: "restaurants");
        }
    }
}
