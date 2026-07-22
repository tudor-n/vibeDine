using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantApi.Migrations
{
    /// <inheritdoc />
    public partial class AddRestaurantBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "brand_background",
                table: "restaurants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "brand_preset_key",
                table: "restaurants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "brand_primary",
                table: "restaurants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "brand_secondary",
                table: "restaurants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "branding_enabled",
                table: "restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "logo_url",
                table: "restaurants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "photo_url",
                table: "restaurants",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "brand_background",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "brand_preset_key",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "brand_primary",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "brand_secondary",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "branding_enabled",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "logo_url",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "photo_url",
                table: "restaurants");
        }
    }
}
