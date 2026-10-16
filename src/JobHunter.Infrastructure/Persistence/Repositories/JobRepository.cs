using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación de <see cref="IJobRepository"/> con EF Core + SQLite.
/// Se registra con ámbito scoped: comparte el DbContext con el resto del
/// pipeline, de modo que las ofertas obtenidas quedan rastreadas.
/// </summary>
internal sealed class JobRepository(JobHunterDbContext db) : IJobRepository
{
    public Task<bool> ExistsAsync(string externalId, string sourceProvider, CancellationToken ct = default)
        => db.JobOffers.AnyAsync(o => o.ExternalId == externalId && o.SourceProvider == sourceProvider, ct);

    public async Task SaveAsync(JobOffer offer, CancellationToken ct = default)
    {
        db.JobOffers.Add(offer);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<JobOffer>> GetUnanalyzedAsync(DateTime discoveredSince, CancellationToken ct = default)
        => await db.JobOffers
            .Where(o => !o.IsAnalyzed && !o.IsDuplicate && o.DiscoveredAt >= discoveredSince)
            .OrderBy(o => o.DiscoveredAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<JobOffer>> GetRecentByCompanyAsync(
        string companyNormalized, int days = 30, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-days);

        return await db.JobOffers
            .Where(o => o.CompanyNormalized == companyNormalized && o.DiscoveredAt >= since)
            .ToListAsync(ct);
    }

    public async Task UpdateAsync(JobOffer offer, CancellationToken ct = default)
    {
        // En el pipeline la oferta se guarda y se consulta en el MISMO scope,
        // así que ya está rastreada: SaveChanges detecta tanto los cambios de
        // propiedades como el JobScore recién asignado (Added). Evitamos
        // db.Update(offer), que marcaría el JobScore nuevo como Modified y
        // fallaría con un UPDATE sobre una fila inexistente (pitfall clásico
        // de las relaciones 1:1 con clave compartida).
        if (db.Entry(offer).State is EntityState.Detached)
        {
            db.Attach(offer).State = EntityState.Modified;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<JobOffer>> GetTopScoredAsync(
        DateTime discoveredSince, int count = 15, CancellationToken ct = default)
        => await db.JobOffers
            .Include(o => o.Score)
            .Where(o => o.Score != null && !o.IsDuplicate && o.DiscoveredAt >= discoveredSince)
            .OrderByDescending(o => o.Score!.TotalScore)
            .Take(count)
            .ToListAsync(ct);

    public async Task SaveReportAsync(DailyReport report, CancellationToken ct = default)
    {
        db.DailyReports.Add(report);
        await db.SaveChangesAsync(ct);
    }

    public async Task<DailyReport?> GetLatestReportAsync(CancellationToken ct = default)
        => await db.DailyReports
            .OrderByDescending(r => r.GeneratedAt)
            .FirstOrDefaultAsync(ct);
}
