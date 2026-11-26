using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiit.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class HomeworkMaxScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "max_score",
                table: "homeworks",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "max_score",
                table: "homeworks");
        }
    }
}
