using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenewableEnergy.Infrastructure.Messaging.Outbox;

namespace RenewableEnergy.Infrastructure.Persistence.Configurations
{
    /// <summary>
    /// Mapeo de la tabla OUTBOX. Esta entidad es puramente técnica (no es
    /// dominio): por eso vive en Infrastructure y no hereda de BaseEntity.
    /// </summary>
    internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
    {
        public void Configure(EntityTypeBuilder<OutboxMessage> builder)
        {
            builder.ToTable("OutboxMessages");

            builder.HasKey(m => m.Id);

            builder.Property(m => m.Type)
                .IsRequired()
                .HasMaxLength(300);

            // El evento serializado completo (JSON con metadatos de tipo).
            builder.Property(m => m.Content)
                .IsRequired();

            builder.Property(m => m.Error)
                .HasMaxLength(4000);

            // El OutboxProcessor consulta "pendientes ordenados por fecha":
            // índice filtrado para que esa query sea barata aunque la tabla crezca.
            builder.HasIndex(m => new { m.ProcessedOnUtc, m.OccurredOnUtc });
        }
    }
}
