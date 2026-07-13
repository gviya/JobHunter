using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobHunter.Infrastructure.Persistence;

/// <summary>
/// Fábrica en TIEMPO DE DISEÑO: la usa <c>dotnet ef</c> para instanciar el
/// contexto al generar/aplicar migraciones sin necesidad de arrancar la
/// aplicación completa. La cadena de conexión aquí es irrelevante para
/// "migrations add" (no se abre conexión); en runtime la configura la API.
/// </summary>
public sealed class JobHunterDbContextFactory : IDesignTimeDbContextFactory<JobHunterDbContext>
{
    public JobHunterDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<JobHunterDbContext>()
            .UseSqlite("Data Source=jobhunter.db")
            .Options;

        return new JobHunterDbContext(options);
    }
}
