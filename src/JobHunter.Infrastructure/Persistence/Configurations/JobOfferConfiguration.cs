using System.Text.Json;
using JobHunter.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace JobHunter.Infrastructure.Persistence.Configurations;

public sealed class JobOfferConfiguration : IEntityTypeConfiguration<JobOffer>
{
    public void Configure(EntityTypeBuilder<JobOffer> builder)
    {
        builder.HasKey(o => o.Id);

        // Deduplicación exacta: no puede haber dos veces la misma oferta
        // de la misma fuente.
        builder.HasIndex(o => new { o.ExternalId, o.SourceProvider }).IsUnique();
        builder.HasIndex(o => o.CompanyNormalized);
        builder.HasIndex(o => o.DiscoveredAt);

        builder.Property(o => o.ExternalId).IsRequired();
        builder.Property(o => o.SourceProvider).IsRequired();
        builder.Property(o => o.Title).IsRequired();
        builder.Property(o => o.CompanyName).IsRequired();
        builder.Property(o => o.Url).IsRequired();

        // El enum se guarda como texto legible ("Remote", "Hybrid"...).
        builder.Property(o => o.WorkMode).HasConversion<string>();

        // SQLite no tiene tipo vector: el embedding se serializa a JSON.
        var embeddingConverter = new ValueConverter<float[]?, string?>(
            v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => string.IsNullOrEmpty(v) ? null : JsonSerializer.Deserialize<float[]>(v, (JsonSerializerOptions?)null));

        var embeddingComparer = new ValueComparer<float[]?>(
            (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
            v => v == null ? 0 : v.Aggregate(0, (hash, f) => HashCode.Combine(hash, f)),
            v => v == null ? null : v.ToArray());

        builder.Property(o => o.Embedding)
            .HasConversion(embeddingConverter, embeddingComparer);

        // Relación 1:1 con JobScore usando clave primaria compartida.
        builder.HasOne(o => o.Score)
            .WithOne()
            .HasForeignKey<JobScore>(s => s.JobOfferId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
