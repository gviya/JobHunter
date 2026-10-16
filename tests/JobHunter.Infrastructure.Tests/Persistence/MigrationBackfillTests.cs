using AwesomeAssertions;
using JobHunter.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace JobHunter.Infrastructure.Tests.Persistence;

/// <summary>
/// Tests de las migraciones de señales de puntuación CON DATOS: parten de una
/// BD en la versión anterior (20260714113629), con filas como las que dejó el
/// código viejo, y migran hasta la última igual que Program.cs. Una BD vacía
/// no detecta nada: los UPDATE del backfill no tocan ninguna fila y la
/// reconstrucción de tabla de SQLite no tiene datos que perder.
/// </summary>
public sealed class MigrationBackfillTests : IAsyncLifetime
{
    private const string BeforeScoringSignals = "20260714113629_AddScoringInfoAndRequirements";

    // Ids fijos en el formato en que EF guarda un Guid en SQLite (TEXT en
    // mayúsculas): así las filas insertadas a mano se leen luego con EF.
    private const string AdzunaId = "A0000000-0000-0000-0000-000000000001";
    private const string JoobleId = "A0000000-0000-0000-0000-000000000002";
    private const string JSearchId = "A0000000-0000-0000-0000-000000000003";
    private const string GreenhouseId = "A0000000-0000-0000-0000-000000000004";
    private const string LeverId = "A0000000-0000-0000-0000-000000000005";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public async Task InitializeAsync()
    {
        // La conexión se mantiene abierta: con :memory: la BD vive lo que ella.
        await _connection.OpenAsync();
        await using var db = CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(BeforeScoringSignals);

        // Ofertas tal y como las guardaba el código viejo: IsClientFinal era un
        // bool NOT NULL, así que los agregadores nacían "consultora" (0).
        await InsertOfferAsync(db, AdzunaId, "Adzuna", isClientFinal: false);
        await InsertOfferAsync(db, JoobleId, "Jooble", isClientFinal: false);
        await InsertOfferAsync(db, JSearchId, "JSearch", isClientFinal: false);
        await InsertOfferAsync(db, GreenhouseId, "Greenhouse", isClientFinal: true);
        await InsertOfferAsync(db, LeverId, "Lever", isClientFinal: true);

        // Puntuaciones con las reglas viejas. Adzuna: consultora -10 y extracto
        // de ~500 caracteres -10. JSearch: consultora -10, pero su descripción
        // es completa, así que su -25 por corta es legítimo y debe quedarse.
        // Greenhouse: cliente final +20 y +8 por bien detallada, ya correctos.
        await InsertScoreAsync(db, AdzunaId, total: 60, clientFinal: -10, infoQuality: -10);
        await InsertScoreAsync(db, JSearchId, total: 50, clientFinal: -10, infoQuality: -25);
        await InsertScoreAsync(db, GreenhouseId, total: 100, clientFinal: 20, infoQuality: 8);
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task MigrarALaUltima_ConDatosDeLaVersionAnterior_RellenaLasSenalesYConservaLasFilas()
    {
        // Regresión (integración): con el UPDATE a NULL dentro de la migración
        // de esquema, SQLite lo ejecuta ANTES de la reconstrucción aplazada que
        // hace nullable IsClientFinal, choca con el NOT NULL viejo y la
        // migración entera falla. Aquí MigrateAsync lanzaría.
        await using (var db = CreateContext())
        {
            await db.Database.MigrateAsync();
        }

        (await ReadIsClientFinalAsync()).Should().BeEquivalentTo(new Dictionary<string, long?>
        {
            ["Adzuna"] = null,
            ["Jooble"] = null,
            ["JSearch"] = null,
            ["Greenhouse"] = 1,
            ["Lever"] = 1,
        });

        (await ReadDescriptionIsSnippetAsync()).Should().BeEquivalentTo(new Dictionary<string, long>
        {
            ["Adzuna"] = 1,
            ["Jooble"] = 1,
            ["JSearch"] = 0,
            ["Greenhouse"] = 0,
            ["Lever"] = 0,
        });

        await AssertForeignKeyIntactAsync();

        // El modelo actual lee las filas migradas (bool? null, no un 0 colado)
        // y la relación 1:1 con su puntuación sigue resolviéndose.
        await using (var db = CreateContext())
        {
            var adzuna = await db.JobOffers.Include(o => o.Score).SingleAsync(o => o.Id == Guid.Parse(AdzunaId));

            adzuna.IsClientFinal.Should().BeNull();
            adzuna.DescriptionIsSnippet.Should().BeTrue();
            adzuna.Score.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task MigrarALaUltima_ConPuntuacionesGuardadas_LasRebasaALasReglasNuevas()
    {
        // Regresión: el backfill cambiaba las señales de la oferta pero no las
        // puntuaciones ya guardadas, así que durante la ventana del informe
        // (7 días) las ofertas de agregador analizadas antes de actualizar
        // seguían 20-35 puntos por debajo de otras idénticas puntuadas con las
        // reglas nuevas, y la fila se contradecía (IsClientFinal "no se sabe"
        // con ClientFinalScore de consultora).
        await using (var db = CreateContext())
        {
            await db.Database.MigrateAsync();
        }

        // Adzuna: ahora "no se sabe" y extracto, ambos cuentan 0 → 60 + 10 + 10.
        (await ReadScoreAsync(AdzunaId)).Should().Be((80, 0, 0));

        // JSearch: solo cambia el cliente final; su descripción no es un
        // extracto, así que la penalización por corta se mantiene.
        (await ReadScoreAsync(JSearchId)).Should().Be((60, 0, -25));

        // Greenhouse (ATS) conserva su dato: no hay nada que rebasar.
        (await ReadScoreAsync(GreenhouseId)).Should().Be((100, 20, 8));
    }

    [Fact]
    public async Task RevertirALaVersionAnterior_TrasMigrar_DegradaLosNullAFalseYConservaLasFilas()
    {
        await using (var db = CreateContext())
        {
            await db.Database.MigrateAsync();
            await db.GetService<IMigrator>().MigrateAsync(BeforeScoringSignals);
        }

        // La columna vuelve a ser NOT NULL: el "no se sabe" se degrada a false
        // (el valor que tenía antes) y los ATS conservan el suyo.
        (await ReadIsClientFinalAsync()).Should().BeEquivalentTo(new Dictionary<string, long?>
        {
            ["Adzuna"] = 0,
            ["Jooble"] = 0,
            ["JSearch"] = 0,
            ["Greenhouse"] = 1,
            ["Lever"] = 1,
        });

        var columns = await ReadJobOfferColumnsAsync();
        columns.Should().NotContainKey("DescriptionIsSnippet");
        columns["IsClientFinal"].Should().BeTrue("la columna vuelve a ser NOT NULL");

        (await CountAsync("SELECT COUNT(*) FROM JobScores")).Should().Be(3);
        await AssertForeignKeyIntactAsync();
    }

    private JobHunterDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<JobHunterDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new JobHunterDbContext(options);
    }

    // SQL a mano con las columnas de la versión 20260714113629: el modelo de
    // EF ya es el nuevo (bool?, DescriptionIsSnippet) y no sirve para
    // escribir en el esquema viejo.
    private static Task InsertOfferAsync(JobHunterDbContext db, string id, string provider, bool isClientFinal) =>
        db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO JobOffers (Id, ExternalId, SourceProvider, Title, CompanyName, CompanyNormalized, Url,
                                   Description, WorkMode, IsClientFinal, PublishedAt, DiscoveredAt, IsAnalyzed, IsDuplicate)
            VALUES ({0}, {1}, {2}, 'Backend .NET', 'Acme', 'acme', {3},
                    'Descripción', 'Remote', {4}, '2026-10-14 08:00:00', '2026-10-14 09:00:00', 1, 0)
            """,
            id, $"ext-{provider}", provider, $"https://example.com/{provider}", isClientFinal ? 1 : 0);

    private static Task InsertScoreAsync(JobHunterDbContext db, string offerId, int total, int clientFinal, int infoQuality) =>
        db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO JobScores (JobOfferId, TotalScore, CompatibilityScore, RemoteScore, SalaryScore, TechScore,
                                   ClientFinalScore, InfoQualityScore, ScoredAt)
            VALUES ({0}, {1}, 80, 30, -5, 15, {2}, {3}, '2026-10-14 09:05:00')
            """,
            offerId, total, clientFinal, infoQuality);

    private async Task<Dictionary<string, long?>> ReadIsClientFinalAsync()
    {
        var values = new Dictionary<string, long?>();
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT SourceProvider, IsClientFinal FROM JobOffers";
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            values[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetInt64(1);
        }

        return values;
    }

    private async Task<Dictionary<string, long>> ReadDescriptionIsSnippetAsync()
    {
        var values = new Dictionary<string, long>();
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT SourceProvider, DescriptionIsSnippet FROM JobOffers";
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            values[reader.GetString(0)] = reader.GetInt64(1);
        }

        return values;
    }

    private async Task<(long Total, long ClientFinal, long InfoQuality)> ReadScoreAsync(string offerId)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT TotalScore, ClientFinalScore, InfoQualityScore FROM JobScores WHERE JobOfferId = $id";
        command.Parameters.AddWithValue("$id", offerId);
        await using var reader = await command.ExecuteReaderAsync();

        (await reader.ReadAsync()).Should().BeTrue($"la puntuación de {offerId} debe seguir existiendo");
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    /// <summary>Columnas de JobOffers → si son NOT NULL.</summary>
    private async Task<Dictionary<string, bool>> ReadJobOfferColumnsAsync()
    {
        var columns = new Dictionary<string, bool>();
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT name, \"notnull\" FROM pragma_table_info('JobOffers')";
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            columns[reader.GetString(0)] = reader.GetInt64(1) == 1;
        }

        return columns;
    }

    private async Task<long> CountAsync(string sql)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// La reconstrucción de JobOffers (crear tabla temporal, copiar, borrar,
    /// renombrar) se hace con las FK desactivadas: hay que comprobar que la
    /// FK de JobScores sigue apuntando a JobOffers con su cascada y que
    /// ninguna puntuación se ha quedado huérfana.
    /// </summary>
    private async Task AssertForeignKeyIntactAsync()
    {
        (await CountAsync("SELECT COUNT(*) FROM pragma_foreign_key_check")).Should().Be(0);
        (await CountAsync(
                "SELECT COUNT(*) FROM pragma_foreign_key_list('JobScores') " +
                "WHERE \"table\" = 'JobOffers' AND \"from\" = 'JobOfferId' AND on_delete = 'CASCADE'"))
            .Should().Be(1);
        (await CountAsync("SELECT COUNT(*) FROM JobScores s JOIN JobOffers o ON o.Id = s.JobOfferId"))
            .Should().Be(3);
    }
}
