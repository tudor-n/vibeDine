using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantApi.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderClaimTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "claimed_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "claimed_by_waiter_id",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_claimed_by_waiter_id",
                table: "orders",
                column: "claimed_by_waiter_id");

            migrationBuilder.AddForeignKey(
                name: "FK_orders_AspNetUsers_claimed_by_waiter_id",
                table: "orders",
                column: "claimed_by_waiter_id",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_orders_AspNetUsers_claimed_by_waiter_id",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_claimed_by_waiter_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "claimed_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "claimed_by_waiter_id",
                table: "orders");
        }
    }
}
