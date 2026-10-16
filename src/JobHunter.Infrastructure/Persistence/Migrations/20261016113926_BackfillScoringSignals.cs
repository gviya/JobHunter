using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Backfill de los datos ya guardados para las columnas de la migración
    /// anterior, y de las puntuaciones calculadas con las reglas viejas.
    /// Va en una migración APARTE porque en SQLite EF aplaza al
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

            // Las puntuaciones ya guardadas llevan las reglas viejas dentro de
            // TotalScore (-10 por "consultora", -10/-25 por un extracto corto) y
            // nada vuelve a puntuar una oferta analizada: sin rebasarlas, el
            // informe (ordenado por TotalScore en una ventana de 7 días) hunde
            // las de agregador analizadas antes de actualizar frente a otras
            // idénticas puntuadas ya con las reglas nuevas. Se resta el
            // componente viejo y se deja en el 0 que dan ahora; SQLite evalúa
            // cada SET con los valores previos de la fila, así que la resta usa
            // el componente original. Van después de los UPDATE de arriba porque
            // filtran por las señales ya rellenadas.
            migrationBuilder.Sql(
                "UPDATE JobScores SET TotalScore = TotalScore - ClientFinalScore, ClientFinalScore = 0 " +
                "WHERE JobOfferId IN (SELECT Id FROM JobOffers WHERE IsClientFinal IS NULL);");
            migrationBuilder.Sql(
                "UPDATE JobScores SET TotalScore = TotalScore - InfoQualityScore, InfoQualityScore = 0 " +
                "WHERE JobOfferId IN (SELECT Id FROM JobOffers WHERE DescriptionIsSnippet = 1);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nada que deshacer aquí: el Down de la migración anterior elimina
            // DescriptionIsSnippet y degrada los NULL de IsClientFinal a false.
            // Las puntuaciones rebasadas se quedan como están: el componente
            // viejo no se puede recuperar y la ventana del informe lo absorbe.
        }
    }
}
