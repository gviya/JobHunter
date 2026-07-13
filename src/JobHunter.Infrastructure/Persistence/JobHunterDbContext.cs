using JobHunter.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence;

/// <summary>
/// DbContext de JobHunter sobre SQLite. Las configuraciones de cada
/// entidad viven en Persistence/Configurations y se descubren por
/// reflexión.
/// </summary>
public sealed class JobHunterDbContext(DbContextOptions<JobHunterDbContext> options) : DbContext(options)
{
    public DbSet<JobOffer> JobOffers => Set<JobOffer>();
    public DbSet<JobScore> JobScores => Set<JobScore>();
    public DbSet<DailyReport> DailyReports => Set<DailyReport>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(JobHunterDbContext).Assembly);
    }
}
