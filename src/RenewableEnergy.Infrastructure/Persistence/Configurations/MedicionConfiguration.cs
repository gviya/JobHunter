using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenewableEnergy.Domain.Entities;

namespace RenewableEnergy.Infrastructure.Persistence.Configurations
{
    /// <summary>Mapeo Fluent API de la entidad hija Medicion.</summary>
    internal sealed class MedicionConfiguration : IEntityTypeConfiguration<Medicion>
    {
        public void Configure(EntityTypeBuilder<Medicion> builder)
        {
            builder.ToTable("Mediciones");

            builder.HasKey(m => m.Id);

            // CLAVE GENERADA EN EL CLIENTE: el Guid lo asigna BaseEntity al
            // construir la entidad, NUNCA la base de datos. Sin esta línea,
            // EF asumiría "Guid = lo genera el store" y trataría como
            // EXISTENTES (UPDATE) a las mediciones nuevas que descubre por
            // navegación desde un proyecto ya trackeado — dando el clásico
            // DbUpdateConcurrencyException "affected 0 rows".
            builder.Property(m => m.Id).ValueGeneratedNever();

            // PRECISIÓN DECIMAL EXPLÍCITA: sin esto, SQL Server usaría el
            // default y EF avisaría con un warning de posible truncamiento.
            builder.Property(m => m.EnergiaGeneradaKWh)
                .IsRequired()
                .HasPrecision(18, 3);

            builder.Property(m => m.FechaMedicion)
                .IsRequired();

            // Índice para la query más frecuente: "mediciones del proyecto X
            // ordenadas por fecha". Sin él, table scan.
            builder.HasIndex(m => new { m.ProyectoRenovableId, m.FechaMedicion });
        }
    }
}
