using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infra.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InheritOutboxMessageFromEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_outbox_messages",
                table: "outbox_messages");

            migrationBuilder.RenameColumn(
                name: "occurred_at",
                table: "outbox_messages",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "event_id",
                table: "outbox_messages",
                newName: "id");

            migrationBuilder.RenameIndex(
                name: "IX_outbox_messages_occurred_at",
                table: "outbox_messages",
                newName: "IX_outbox_messages_created_at");

            migrationBuilder.AddColumn<int>(
                name: "internal_id",
                table: "outbox_messages",
                type: "integer",
                nullable: false)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "outbox_messages",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // Keep existing messages active, then remove the temporary backfill default.
            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                table: "outbox_messages",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_outbox_messages",
                table: "outbox_messages",
                column: "internal_id");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_id",
                table: "outbox_messages",
                column: "id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_outbox_messages",
                table: "outbox_messages");

            migrationBuilder.DropIndex(
                name: "IX_outbox_messages_id",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "internal_id",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "outbox_messages");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "outbox_messages",
                newName: "event_id");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "outbox_messages",
                newName: "occurred_at");

            migrationBuilder.RenameIndex(
                name: "IX_outbox_messages_created_at",
                table: "outbox_messages",
                newName: "IX_outbox_messages_occurred_at");

            migrationBuilder.AddPrimaryKey(
                name: "PK_outbox_messages",
                table: "outbox_messages",
                column: "event_id");
        }
    }
}
