using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LiveLeaderboard.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectionAttemptTimestamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastAttemptAtUtc",
                table: "ProjectionOutboxes",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastAttemptAtUtc",
                table: "ProjectionOutboxes");
        }
    }
}
