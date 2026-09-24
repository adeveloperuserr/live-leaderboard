using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LiveLeaderboard.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScoreEventTotalSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TotalAfterScore",
                table: "ScoreEvents",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalAfterScore",
                table: "ScoreEvents");
        }
    }
}
