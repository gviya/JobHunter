using Microsoft.EntityFrameworkCore;
using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Infrastructure.Messaging.Outbox;

namespace RenewableEnergy.Infrastructure.Persistence
{
    /// <summary>
    /// DbContext de EF Core: la "sesión" con la base de datos.
    ///
    /// FÍJATE EN LO LIMPIO QUE QUEDA:
    /// - El mapeo NO está aquí: cada entidad tiene su clase de configuración
    ///   Fluent API en Persistence/Configurations (ApplyConfigurationsFromAssembly
    ///   las descubre todas).
    /// - La auditoría (CreatedAt/UpdatedAt) NO está aquí: la aplica el
    ///   AuditInterceptor.
    /// - El volcado de eventos de dominio al outbox NO está aquí: lo hace el
    ///   OutboxInterceptor. Los interceptors se registran al configurar el
    ///   DbContext en DependencyInjection.
    /// </summary>
    public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : DbContext(options)
    {
        public DbSet<ProyectoRenovable> Proyectos => Set<ProyectoRenovable>();
        public DbSet<Medicion> Mediciones => Set<Medicion>();
        public DbSet<Instalacion> Instalaciones => Set<Instalacion>();
        public DbSet<Usuario> Usuarios => Set<Usuario>();
        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Descubre y aplica TODAS las IEntityTypeConfiguration de este
            // assembly (ProyectoConfiguration, MedicionConfiguration...).
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

            base.OnModelCreating(modelBuilder);
        }
    }
}
