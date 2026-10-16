using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDescriptionIsSnippet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DescriptionIsSnippet",
                table: "JobOffers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Backfill: las ofertas de Adzuna y Jooble ya guardadas también son
            // extractos; así las pendientes de análisis que se reintentan no se
            // penalizan por la longitud de la descripción.
            migrationBuilder.Sql("UPDATE JobOffers SET DescriptionIsSnippet = 1 WHERE SourceProvider IN ('Adzuna', 'Jooble');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DescriptionIsSnippet",
                table: "JobOffers");
        }
    }
}
