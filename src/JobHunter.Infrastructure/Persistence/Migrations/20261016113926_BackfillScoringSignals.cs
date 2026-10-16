using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Backfill de los datos ya guardados para las columnas de la migración
    /// anterior. Va en una migración APARTE porque en SQLite EF aplaza al
    /// final de cada migración la reconstrucción de tabla que hace nullable
    /// IsClientFinal: un UPDATE a NULL en la misma migración choca con el
    /// NOT NULL antiguo y la revierte entera.
    /// </summary>
    public partial class BackfillScoringSignals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Adzuna y Jooble solo dan extractos: así las pendientes que se
            // reintentan no se penalizan por la longitud de la descripción.
            migrationBuilder.Sql("UPDATE JobOffers SET DescriptionIsSnippet = 1 WHERE SourceProvider IN ('Adzuna', 'Jooble');");

            // El "false" de las ofertas que no vienen de un ATS no era un dato
            // sino el valor por defecto del bool: pasa a "no se sabe".
            migrationBuilder.Sql("UPDATE JobOffers SET IsClientFinal = NULL WHERE SourceProvider NOT IN ('Greenhouse', 'Lever');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nada que deshacer aquí: el Down de la migración anterior elimina
            // DescriptionIsSnippet y degrada los NULL de IsClientFinal a false.
        }
    }
}
