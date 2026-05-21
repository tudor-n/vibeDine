using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantApi.Migrations
{
    /// <inheritdoc />
    public partial class AddWaiterRoleAvatarAndSignupLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "memberships",
                type: "text",
                nullable: false,
                defaultValue: "active");

            migrationBuilder.AddColumn<string>(
                name: "avatar_path",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "waiter_signup_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_waiter_signup_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_waiter_signup_links_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_waiter_signup_links_restaurants_restaurant_id",
                        column: x => x.restaurant_id,
                        principalTable: "restaurants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_waiter_signup_links_organization_id",
                table: "waiter_signup_links",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_waiter_signup_links_restaurant_id",
                table: "waiter_signup_links",
                column: "restaurant_id");

            migrationBuilder.CreateIndex(
                name: "IX_waiter_signup_links_token",
                table: "waiter_signup_links",
                column: "token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "waiter_signup_links");

            migrationBuilder.DropColumn(
                name: "status",
                table: "memberships");

            migrationBuilder.DropColumn(
                name: "avatar_path",
                table: "AspNetUsers");
        }
    }
}
