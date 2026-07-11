using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.ValueObjects;

namespace RenewableEnergy.Infrastructure.Persistence.Configurations
{
    /// <summary>Mapeo Fluent API de Usuario, con su Value Object Email.</summary>
    internal sealed class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
    {
        public void Configure(EntityTypeBuilder<Usuario> builder)
        {
            builder.ToTable("Usuarios");

            builder.HasKey(u => u.Id);

            builder.Property(u => u.Nombre)
                .IsRequired()
                .HasMaxLength(200);

            // El VO Email se guarda como string y se reconstruye (validándose)
            // al leer. La columna queda con índice único: un email, un usuario.
            builder.Property(u => u.Email)
                .IsRequired()
                .HasConversion(
                    email => email.Valor,
                    valor => Email.Create(valor))
                .HasMaxLength(320);

            builder.HasIndex(u => u.Email)
                .IsUnique();

            builder.Property(u => u.PasswordHash)
                .IsRequired()
                .HasMaxLength(500);
        }
    }
}
