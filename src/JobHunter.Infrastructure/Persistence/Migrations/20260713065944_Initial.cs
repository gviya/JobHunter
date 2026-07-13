using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TotalFound = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalNew = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalDuplicates = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalAnalyzed = table.Column<int>(type: "INTEGER", nullable: false),
                    HtmlContent = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", nullable: false),
                    SourceProvider = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    CompanyName = table.Column<string>(type: "TEXT", nullable: false),
                    CompanyNormalized = table.Column<string>(type: "TEXT", nullable: true),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    SalaryMin = table.Column<decimal>(type: "TEXT", nullable: true),
                    SalaryMax = table.Column<decimal>(type: "TEXT", nullable: true),
                    WorkMode = table.Column<string>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: true),
                    IsClientFinal = table.Column<bool>(type: "INTEGER", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DiscoveredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsAnalyzed = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDuplicate = table.Column<bool>(type: "INTEGER", nullable: false),
                    Embedding = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobOffers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobScores",
                columns: table => new
                {
                    JobOfferId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TotalScore = table.Column<int>(type: "INTEGER", nullable: false),
                    CompatibilityScore = table.Column<int>(type: "INTEGER", nullable: false),
                    RemoteScore = table.Column<int>(type: "INTEGER", nullable: false),
                    SalaryScore = table.Column<int>(type: "INTEGER", nullable: false),
                    TechScore = table.Column<int>(type: "INTEGER", nullable: false),
                    ClientFinalScore = table.Column<int>(type: "INTEGER", nullable: false),
                    AiSummary = table.Column<string>(type: "TEXT", nullable: true),
                    AiSeniority = table.Column<string>(type: "TEXT", nullable: true),
                    AiTechList = table.Column<string>(type: "TEXT", nullable: true),
                    AiRecommendation = table.Column<string>(type: "TEXT", nullable: true),
                    ScoredAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobScores", x => x.JobOfferId);
                    table.ForeignKey(
                        name: "FK_JobScores_JobOffers_JobOfferId",
                        column: x => x.JobOfferId,
                        principalTable: "JobOffers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobOffers_CompanyNormalized",
                table: "JobOffers",
                column: "CompanyNormalized");

            migrationBuilder.CreateIndex(
                name: "IX_JobOffers_DiscoveredAt",
                table: "JobOffers",
                column: "DiscoveredAt");

            migrationBuilder.CreateIndex(
                name: "IX_JobOffers_ExternalId_SourceProvider",
                table: "JobOffers",
                columns: new[] { "ExternalId", "SourceProvider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyReports");

            migrationBuilder.DropTable(
                name: "JobScores");

            migrationBuilder.DropTable(
                name: "JobOffers");
        }
    }
}
