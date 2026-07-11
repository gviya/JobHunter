using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RenewableEnergy.Infrastructure.Persistence
{
    /// <summary>
    /// Fábrica DE TIEMPO DE DISEÑO para `dotnet ef`.
    ///
    /// ¿POR QUÉ EXISTE?
    /// Cuando ejecutas `dotnet ef migrations add ...`, la herramienta necesita
    /// construir un DbContext SIN arrancar la aplicación completa (sin Serilog,
    /// sin MassTransit, sin Key Vault...). Esta clase le da un contexto mínimo
    /// apuntando a una cadena de conexión local. La cadena NO se usa en
    /// runtime: solo para que EF genere el código de las migraciones.
    /// </summary>
    public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

            // Solo diseño: para generar migraciones no hace falta que la BD exista.
            optionsBuilder.UseSqlServer(
                "Server=localhost,1433;Database=RenewableEnergyDb;User Id=sa;Password=Dev_Password123!;TrustServerCertificate=True;");

            return new ApplicationDbContext(optionsBuilder.Options);
        }
    }
}
