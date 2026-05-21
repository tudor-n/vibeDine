using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantApi.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuImportJobLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "lease_id",
                table: "menu_import_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "lease_expires_at",
                table: "menu_import_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_heartbeat_at",
                table: "menu_import_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "processing_attempts",
                table: "menu_import_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_menu_import_jobs_status_lease_expires_at",
                table: "menu_import_jobs",
                columns: new[] { "status", "lease_expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_menu_import_jobs_status_lease_expires_at",
                table: "menu_import_jobs");

            migrationBuilder.DropColumn(
                name: "lease_id",
                table: "menu_import_jobs");

            migrationBuilder.DropColumn(
                name: "lease_expires_at",
                table: "menu_import_jobs");

            migrationBuilder.DropColumn(
                name: "last_heartbeat_at",
                table: "menu_import_jobs");

            migrationBuilder.DropColumn(
                name: "processing_attempts",
                table: "menu_import_jobs");
        }
    }
}
