namespace RenewableEnergy.Domain.Common
{
    /// <summary>
    /// Clase base de TODAS las entidades del dominio.
    ///
    /// ¿QUÉ APORTA?
    /// - <see cref="Id"/>: identidad única (Guid). Dos entidades son "la misma"
    ///   si comparten Id, aunque sus datos difieran (identidad vs igualdad).
    /// - <see cref="CreatedAt"/> / <see cref="UpdatedAt"/>: auditoría. OJO:
    ///   sus setters son privados; los rellena automáticamente el
    ///   AuditInterceptor de Infrastructure al hacer SaveChanges. El dominio
    ///   no se ensucia con esa mecánica.
    /// - Gestión de EVENTOS DE DOMINIO: la entidad acumula los hechos que
    ///   ocurren (RaiseDomainEvent) y la infraestructura los recoge al
    ///   persistir (patrón Outbox).
    ///
    /// ¿POR QUÉ NO HAY ATRIBUTOS [Required], [MaxLength]...?
    /// Porque las DataAnnotations acoplan el dominio a la persistencia.
    /// El mapeo a base de datos se define con Fluent API en
    /// Infrastructure/Persistence/Configurations.
    /// </summary>
    public abstract class BaseEntity
    {
        private readonly List<IDomainEvent> _domainEvents = [];

        /// <summary>Identidad única de la entidad. Se genera al construirla.</summary>
        public Guid Id { get; protected set; } = Guid.NewGuid();

        /// <summary>Fecha de creación (UTC). La escribe el AuditInterceptor.</summary>
        public DateTime CreatedAt { get; private set; }

        /// <summary>Fecha de última modificación (UTC). La escribe el AuditInterceptor.</summary>
        public DateTime? UpdatedAt { get; private set; }

        /// <summary>
        /// Devuelve una COPIA de los eventos pendientes. Se devuelve copia para
        /// que nadie pueda mutar la lista interna desde fuera (encapsulación).
        /// </summary>
        public IReadOnlyList<IDomainEvent> GetDomainEvents()
        {
            return [.. _domainEvents];
        }

        /// <summary>
        /// Vacía los eventos pendientes. Lo llama Infrastructure justo después
        /// de copiarlos a la tabla Outbox, para no procesarlos dos veces.
        /// </summary>
        public void ClearDomainEvents()
        {
            _domainEvents.Clear();
        }

        /// <summary>
        /// Registra un hecho de negocio. Lo usan las entidades hijas en sus
        /// métodos de negocio: p. ej. ProyectoRenovable.Activar() levanta
        /// un ProyectoActivado.
        /// </summary>
        protected void RaiseDomainEvent(IDomainEvent domainEvent)
        {
            _domainEvents.Add(domainEvent);
        }
    }
}
