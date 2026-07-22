using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantApi.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "device_session_id",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "device_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_device_sessions_tables_table_id",
                        column: x => x.table_id,
                        principalTable: "tables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_orders_device_session_id",
                table: "orders",
                column: "device_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_device_sessions_restaurant_id",
                table: "device_sessions",
                column: "restaurant_id");

            migrationBuilder.CreateIndex(
                name: "IX_device_sessions_table_id_device_id",
                table: "device_sessions",
                columns: new[] { "table_id", "device_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_orders_device_sessions_device_session_id",
                table: "orders",
                column: "device_session_id",
                principalTable: "device_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_orders_device_sessions_device_session_id",
                table: "orders");

            migrationBuilder.DropTable(
                name: "device_sessions");

            migrationBuilder.DropIndex(
                name: "IX_orders_device_session_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "device_session_id",
                table: "orders");
        }
    }
}
