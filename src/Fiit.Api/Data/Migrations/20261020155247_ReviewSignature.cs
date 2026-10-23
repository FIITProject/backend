using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiit.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReviewSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_reviews_teachers_teacher_id",
                table: "reviews");

            migrationBuilder.RenameColumn(
                name: "teacher_id",
                table: "reviews",
                newName: "updated_by_id");

            migrationBuilder.RenameIndex(
                name: "ix_reviews_teacher_id",
                table: "reviews",
                newName: "ix_reviews_updated_by_id");

            migrationBuilder.AddColumn<int>(
                name: "sent_by_id",
                table: "reviews",
                type: "integer",
                nullable: true);

            // Уже отправленные проверки подписываем тем, кто последним их правил
            migrationBuilder.Sql("UPDATE reviews SET sent_by_id = updated_by_id WHERE sent_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_sent_by_id",
                table: "reviews",
                column: "sent_by_id");

            migrationBuilder.AddForeignKey(
                name: "fk_reviews_teachers_sent_by_id",
                table: "reviews",
                column: "sent_by_id",
                principalTable: "teachers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_reviews_teachers_updated_by_id",
                table: "reviews",
                column: "updated_by_id",
                principalTable: "teachers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_reviews_teachers_sent_by_id",
                table: "reviews");

            migrationBuilder.DropForeignKey(
                name: "fk_reviews_teachers_updated_by_id",
                table: "reviews");

            migrationBuilder.DropIndex(
                name: "ix_reviews_sent_by_id",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "sent_by_id",
                table: "reviews");

            migrationBuilder.RenameColumn(
                name: "updated_by_id",
                table: "reviews",
                newName: "teacher_id");

            migrationBuilder.RenameIndex(
                name: "ix_reviews_updated_by_id",
                table: "reviews",
                newName: "ix_reviews_teacher_id");

            migrationBuilder.AddForeignKey(
                name: "fk_reviews_teachers_teacher_id",
                table: "reviews",
                column: "teacher_id",
                principalTable: "teachers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
