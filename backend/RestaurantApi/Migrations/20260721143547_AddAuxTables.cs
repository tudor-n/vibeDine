using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantApi.Migrations
{
    /// <inheritdoc />
    public partial class AddAuxTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "assigned_waiter_id",
                table: "tables",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_aux",
                table: "tables",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "moved_from_table_id",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "moved_to_aux_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tables_assigned_waiter_id",
                table: "tables",
                column: "assigned_waiter_id");

            migrationBuilder.CreateIndex(
                name: "IX_tables_restaurant_id_assigned_waiter_id",
                table: "tables",
                columns: new[] { "restaurant_id", "assigned_waiter_id" },
                unique: true,
                filter: "is_aux AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_orders_moved_from_table_id",
                table: "orders",
                column: "moved_from_table_id");

            migrationBuilder.AddForeignKey(
                name: "FK_orders_tables_moved_from_table_id",
                table: "orders",
                column: "moved_from_table_id",
                principalTable: "tables",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_tables_AspNetUsers_assigned_waiter_id",
                table: "tables",
                column: "assigned_waiter_id",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_orders_tables_moved_from_table_id",
                table: "orders");

            migrationBuilder.DropForeignKey(
                name: "FK_tables_AspNetUsers_assigned_waiter_id",
                table: "tables");

            migrationBuilder.DropIndex(
                name: "IX_tables_assigned_waiter_id",
                table: "tables");

            migrationBuilder.DropIndex(
                name: "IX_tables_restaurant_id_assigned_waiter_id",
                table: "tables");

            migrationBuilder.DropIndex(
                name: "IX_orders_moved_from_table_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "assigned_waiter_id",
                table: "tables");

            migrationBuilder.DropColumn(
                name: "is_aux",
                table: "tables");

            migrationBuilder.DropColumn(
                name: "moved_from_table_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "moved_to_aux_at",
                table: "orders");
        }
    }
}
