namespace RenewableEnergy.Domain.Interfaces
{
    /// <summary>
    /// Contrato del patrón UNIT OF WORK.
    ///
    /// ¿QUÉ ES?
    /// Una "unidad de trabajo" agrupa todos los cambios de una operación de
    /// negocio (añadir un proyecto, sus instalaciones, sus mensajes outbox...)
    /// y los confirma de forma ATÓMICA: o se guarda todo o no se guarda nada.
    ///
    /// ¿POR QUÉ LA INTERFAZ VIVE EN DOMAIN?
    /// Principio de INVERSIÓN DE DEPENDENCIAS: el dominio define QUÉ necesita
    /// ("confírmame los cambios") e Infrastructure decide CÓMO (EF Core y su
    /// DbContext.SaveChangesAsync). Así Application/Domain no referencian EF.
    ///
    /// REGLA PRÁCTICA: los repositorios NO guardan (Add solo marca en memoria);
    /// el handler termina llamando UNA VEZ a SaveChangesAsync. Una transacción
    /// por caso de uso.
    /// </summary>
    public interface IUnitOfWork
    {
        /// <summary>
        /// Confirma todos los cambios pendientes en una única transacción.
        /// Devuelve el número de filas afectadas.
        /// </summary>
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
