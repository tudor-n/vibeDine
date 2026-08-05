using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RestaurantApi.Migrations
{
    /// <summary>
    /// Waiter mode: the table_service_sessions domain (with the partial unique index that
    /// makes an open table claim exclusive at the database level), the three new orders
    /// columns (table_service_session_id / source / placed_by_user_id), order_items.notes,
    /// and the order_events outbox the separate LogisticApi consumes.
    ///
    /// The emitted SQL matches the frozen cross-repo contract. The only additions beyond it
    /// are EF's automatic foreign-key support indexes (IX_orders_placed_by_user_id,
    /// IX_table_service_sessions_waiter_user_id) — a mechanical consequence of declaring
    /// those FKs, and useful in their own right ("this waiter's sessions/orders").
    /// table_id needs no such index: the partial unique index above already covers it.
    /// </summary>
    public partial class AddWaiterModeAndOrderEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "placed_by_user_id",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "orders",
                type: "text",
                nullable: false,
                defaultValue: "qr");

            migrationBuilder.AddColumn<Guid>(
                name: "table_service_session_id",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "order_items",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "order_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "table_service_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    waiter_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    waiter_name = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "open"),
                    opened_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_reason = table.Column<string>(type: "text", nullable: true),
                    payment_method = table.Column<string>(type: "text", nullable: true),
                    cash_amount = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    card_amount = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_by_name = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_table_service_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_table_service_sessions_AspNetUsers_waiter_user_id",
                        column: x => x.waiter_user_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_table_service_sessions_tables_table_id",
                        column: x => x.table_id,
                        principalTable: "tables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_orders_placed_by_user_id",
                table: "orders",
                column: "placed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_table_service_session_id",
                table: "orders",
                column: "table_service_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_events_restaurant_id_created_at",
                table: "order_events",
                columns: new[] { "restaurant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_order_events_unprocessed",
                table: "order_events",
                column: "created_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_table_service_sessions_open_table",
                table: "table_service_sessions",
                column: "table_id",
                unique: true,
                filter: "status = 'open'");

            migrationBuilder.CreateIndex(
                name: "ix_table_service_sessions_restaurant_id",
                table: "table_service_sessions",
                column: "restaurant_id");

            migrationBuilder.CreateIndex(
                name: "ix_table_service_sessions_restaurant_id_status",
                table: "table_service_sessions",
                columns: new[] { "restaurant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_table_service_sessions_waiter_user_id",
                table: "table_service_sessions",
                column: "waiter_user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_orders_AspNetUsers_placed_by_user_id",
                table: "orders",
                column: "placed_by_user_id",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_orders_table_service_sessions_table_service_session_id",
                table: "orders",
                column: "table_service_session_id",
                principalTable: "table_service_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_orders_AspNetUsers_placed_by_user_id",
                table: "orders");

            migrationBuilder.DropForeignKey(
                name: "FK_orders_table_service_sessions_table_service_session_id",
                table: "orders");

            migrationBuilder.DropTable(
                name: "order_events");

            migrationBuilder.DropTable(
                name: "table_service_sessions");

            migrationBuilder.DropIndex(
                name: "IX_orders_placed_by_user_id",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_orders_table_service_session_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "placed_by_user_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "source",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "table_service_session_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "notes",
                table: "order_items");
        }
    }
}
