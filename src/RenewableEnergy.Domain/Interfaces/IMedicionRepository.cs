using RenewableEnergy.Domain.Entities;

namespace RenewableEnergy.Domain.Interfaces
{
    /// <summary>
    /// Repositorio de SOLO LECTURA para mediciones.
    ///
    /// ¿POR QUÉ NO TIENE Add/Update/Delete?
    /// Porque Medicion es una entidad HIJA del agregado ProyectoRenovable:
    /// la escritura pasa siempre por la raíz
    /// (<c>proyecto.RegistrarMedicion(...)</c> + <c>IUnitOfWork.SaveChangesAsync()</c>).
    /// Este repositorio existe para el lado QUERY de CQRS: listar mediciones
    /// sin cargar el agregado entero.
    /// </summary>
    public interface IMedicionRepository
    {
        /// <summary>Mediciones de un proyecto ordenadas por fecha descendente.</summary>
        Task<IReadOnlyList<Medicion>> GetByProyectoAsync(Guid proyectoId, CancellationToken cancellationToken = default);
    }
}
