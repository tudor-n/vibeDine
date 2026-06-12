using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTableSoftDelete : Migration
    {
        // NOTE: `dotnet ef migrations add` also emitted a batch of `subcategories`
        // rename/restructure operations here. Those were spurious — they came from
        // a stale model snapshot that was missing the Subcategory entity, not from
        // any real schema change. The `subcategories` table is already created
        // correctly by 20260526105643_AddSubcategories, so replaying that DDL would
        // fail/corrupt an existing database. This migration is therefore trimmed to
        // the only real change: the tables.deleted_at soft-delete column. (The
        // regenerated snapshot keeps the corrected Subcategory entity, which is
        // harmless and stops future migrations from re-detecting the same drift.)

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "tables",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "tables");
        }
    }
}
