using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScoringInfoAndRequirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiRequirements",
                table: "JobScores",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InfoQualityScore",
                table: "JobScores",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiRequirements",
                table: "JobScores");

            migrationBuilder.DropColumn(
                name: "InfoQualityScore",
                table: "JobScores");
        }
    }
}
