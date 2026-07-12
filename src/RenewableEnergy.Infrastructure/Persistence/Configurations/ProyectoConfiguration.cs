using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.ValueObjects;

namespace RenewableEnergy.Infrastructure.Persistence.Configurations
{
    /// <summary>
    /// Mapeo de ProyectoRenovable con FLUENT API.
    ///
    /// ¿POR QUÉ FLUENT API Y NO DATAANNOTATIONS?
    /// Poner [Required]/[MaxLength] en la entidad acoplaría el DOMINIO a la
    /// PERSISTENCIA (la entidad sabría que existe una base de datos). Con
    /// Fluent API el mapeo vive aquí, en Infrastructure, y la entidad queda
    /// pura. Además Fluent API es más potente: owned types, conversiones,
    /// índices compuestos, backing fields...
    /// </summary>
    internal sealed class ProyectoConfiguration : IEntityTypeConfiguration<ProyectoRenovable>
    {
        public void Configure(EntityTypeBuilder<ProyectoRenovable> builder)
        {
            builder.ToTable("Proyectos");

            builder.HasKey(p => p.Id);

            // El Guid lo genera el DOMINIO (BaseEntity), no la base de datos.
            builder.Property(p => p.Id).ValueGeneratedNever();

            builder.Property(p => p.Nombre)
                .IsRequired()
                .HasMaxLength(200);

            // Unicidad de nombre a nivel de BD: la comprobación del handler
            // (ExisteNombreAsync) evita el caso normal, pero el índice único
            // es la garantía definitiva frente a condiciones de carrera.
            builder.HasIndex(p => p.Nombre)
                .IsUnique();

            builder.Property(p => p.Descripcion)
                .HasMaxLength(2000);

            // ENUMS COMO STRING: la tabla es legible ("Solar", "Activo")
            // sin sacrificar el tipado del código.
            builder.Property(p => p.Tipo)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(20);

            builder.Property(p => p.Estado)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(20);

            // VALUE OBJECT DE UN SOLO VALOR → CONVERSIÓN: la columna guarda
            // el decimal y EF reconstruye el VO con su factory al leer
            // (¡la validación se re-ejecuta al materializar!).
            builder.Property(p => p.Potencia)
                .IsRequired()
                .HasConversion(
                    potencia => potencia.Valor,
                    valor => PotenciaKW.Create(valor))
                .HasColumnName("PotenciaKW")
                .HasPrecision(18, 2);

            // VALUE OBJECT DE VARIOS VALORES → OWNED TYPE: dos columnas en la
            // misma tabla Proyectos. EF usa el constructor privado del record.
            builder.OwnsOne(p => p.Ubicacion, ubicacion =>
            {
                ubicacion.Property(u => u.Latitud)
                    .HasColumnName("Latitud")
                    .IsRequired();

                ubicacion.Property(u => u.Longitud)
                    .HasColumnName("Longitud")
                    .IsRequired();
            });

            // RELACIONES 1:N con las entidades hijas del agregado.
            // Cascade: si se borra el proyecto, van fuera sus hijas.
            builder.HasMany(p => p.Mediciones)
                .WithOne()
                .HasForeignKey(m => m.ProyectoRenovableId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(p => p.Instalaciones)
                .WithOne()
                .HasForeignKey(i => i.ProyectoRenovableId)
                .OnDelete(DeleteBehavior.Cascade);

            // BACKING FIELDS: EF lee/escribe las listas privadas _mediciones
            // y _instalaciones, respetando la encapsulación del agregado
            // (la propiedad pública es IReadOnlyCollection sin setter).
            builder.Navigation(p => p.Mediciones)
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            builder.Navigation(p => p.Instalaciones)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        }
    }
}
