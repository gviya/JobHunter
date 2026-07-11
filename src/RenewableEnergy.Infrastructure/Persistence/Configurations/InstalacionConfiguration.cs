using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.ValueObjects;

namespace RenewableEnergy.Infrastructure.Persistence.Configurations
{
    /// <summary>Mapeo Fluent API de la entidad hija Instalacion.</summary>
    internal sealed class InstalacionConfiguration : IEntityTypeConfiguration<Instalacion>
    {
        public void Configure(EntityTypeBuilder<Instalacion> builder)
        {
            builder.ToTable("Instalaciones");

            builder.HasKey(i => i.Id);

            builder.Property(i => i.Nombre)
                .IsRequired()
                .HasMaxLength(200);

            // Mismo patrón que en Proyecto: VO de un valor → conversión.
            builder.Property(i => i.PotenciaInstalada)
                .IsRequired()
                .HasConversion(
                    potencia => potencia.Valor,
                    valor => PotenciaKW.Create(valor))
                .HasColumnName("PotenciaInstaladaKW")
                .HasPrecision(18, 2);
        }
    }
}
