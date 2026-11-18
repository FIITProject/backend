using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiit.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class NotificationsViaRabbitMq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Очередь уведомлений переехала в RabbitMQ: аренда и повторы теперь на стороне бота, в outbox остаётся статус доставки
            migrationBuilder.DropIndex(
                name: "ix_notifications_status_available_at",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_notifications_student_id",
                table: "notifications");

            migrationBuilder.RenameColumn(
                name: "sent_at",
                table: "notifications",
                newName: "delivered_at");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "published_at",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE notifications SET status = 'Delivered' WHERE status = 'Sent'");
            migrationBuilder.Sql("UPDATE notifications SET published_at = COALESCE(delivered_at, created_at) WHERE status <> 'Pending'");

            migrationBuilder.DropColumn(
                name: "attempts",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "available_at",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "locked_until",
                table: "notifications");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_pending",
                table: "notifications",
                column: "id",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_student_id_id",
                table: "notifications",
                columns: new[] { "student_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notifications_pending",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_notifications_student_id_id",
                table: "notifications");

            migrationBuilder.AddColumn<int>(
                name: "attempts",
                table: "notifications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "available_at",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "locked_until",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            // Опубликованное, но не доставленное вернётся в старую очередь в БД
            migrationBuilder.Sql("UPDATE notifications SET status = 'Pending' WHERE status = 'Published'");
            migrationBuilder.Sql("UPDATE notifications SET status = 'Sent' WHERE status = 'Delivered'");

            migrationBuilder.DropColumn(
                name: "published_at",
                table: "notifications");

            migrationBuilder.RenameColumn(
                name: "delivered_at",
                table: "notifications",
                newName: "sent_at");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_status_available_at",
                table: "notifications",
                columns: new[] { "status", "available_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_student_id",
                table: "notifications",
                column: "student_id");
        }
    }
}
