using JobHunter.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Configurations;

public sealed class JobScoreConfiguration : IEntityTypeConfiguration<JobScore>
{
    public void Configure(EntityTypeBuilder<JobScore> builder)
    {
        // Clave primaria compartida con JobOffer (1:1): el valor lo fija la
        // oferta, no lo genera la BD.
        builder.HasKey(s => s.JobOfferId);
        builder.Property(s => s.JobOfferId).ValueGeneratedNever();
    }
}
