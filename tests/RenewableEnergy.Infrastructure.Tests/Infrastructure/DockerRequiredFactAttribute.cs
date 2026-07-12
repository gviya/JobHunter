using System.Diagnostics;

namespace RenewableEnergy.Infrastructure.Tests.Infrastructure
{
    /// <summary>
    /// Detección de Docker, cacheada para toda la ejecución de tests.
    /// Ejecuta "docker info" una única vez (Lazy es thread-safe).
    /// </summary>
    internal static class Docker
    {
        private static readonly Lazy<bool> Deteccion = new(() =>
        {
            try
            {
                using var proceso = Process.Start(new ProcessStartInfo("docker", "info")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                return proceso is not null && proceso.WaitForExit(10_000) && proceso.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        });

        internal static bool Disponible => Deteccion.Value;
    }

    /// <summary>
    /// [Fact] condicional: el test se OMITE (no falla) cuando Docker no está
    /// disponible. Así `dotnet test` queda verde en máquinas sin Docker y
    /// los tests de integración corren de verdad en CI (donde sí lo hay).
    /// </summary>
    public sealed class DockerRequiredFactAttribute : FactAttribute
    {
        public DockerRequiredFactAttribute()
        {
            if (!Docker.Disponible)
            {
                Skip = "Docker no está disponible: test de integración omitido.";
            }
        }
    }
}
