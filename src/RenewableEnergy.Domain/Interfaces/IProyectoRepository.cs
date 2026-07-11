using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.Specifications;

namespace RenewableEnergy.Domain.Interfaces
{
    /// <summary>
    /// CONTRATO del repositorio de proyectos. Aquí NO hay nada de EF Core:
    /// solo lo que el negocio necesita expresado en términos del dominio.
    ///
    /// ¿POR QUÉ LA INTERFAZ ESTÁ EN DOMAIN Y LA IMPLEMENTACIÓN EN INFRASTRUCTURE?
    /// Es el PATRÓN REPOSITORY con inversión de dependencias: los handlers de
    /// Application programan contra esta abstracción, lo que permite:
    /// - Testear los handlers con un mock (sin base de datos).
    /// - Cambiar EF Core por Dapper/Mongo sin tocar Application ni Domain.
    ///
    /// FÍJATE: solo existe repositorio para la RAÍZ del agregado. Las
    /// instalaciones se cargan a través del proyecto; para las mediciones hay
    /// un repositorio de SOLO LECTURA (ver IMedicionRepository).
    /// </summary>
    public interface IProyectoRepository
    {
        /// <summary>Busca un proyecto por Id. Null si no existe.</summary>
        Task<ProyectoRenovable?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Busca un proyecto por Id cargando también sus mediciones e
        /// instalaciones (necesario para operar sobre el agregado completo).
        /// </summary>
        Task<ProyectoRenovable?> GetByIdConDetalleAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>Devuelve los proyectos que cumplen una Specification.</summary>
        Task<IReadOnlyList<ProyectoRenovable>> GetBySpecificationAsync(
            Specification<ProyectoRenovable> specification,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Página de proyectos + total, opcionalmente filtrada por una
        /// Specification. El repositorio devuelve una tupla (no un
        /// PaginatedList) porque ese tipo pertenece a Application y Domain
        /// no puede depender de capas externas.
        /// </summary>
        Task<(IReadOnlyList<ProyectoRenovable> Items, int Total)> GetPaginadoAsync(
            Specification<ProyectoRenovable>? specification,
            int pagina,
            int tamanoPagina,
            CancellationToken cancellationToken = default);

        /// <summary>Comprueba si ya existe un proyecto con ese nombre (regla de unicidad).</summary>
        Task<bool> ExisteNombreAsync(string nombre, Guid? excluirId = null, CancellationToken cancellationToken = default);

        /// <summary>Marca el proyecto para inserción. NO guarda: eso es del IUnitOfWork.</summary>
        void Add(ProyectoRenovable proyecto);

        /// <summary>Marca el proyecto para borrado. NO guarda: eso es del IUnitOfWork.</summary>
        void Delete(ProyectoRenovable proyecto);
    }
}
