using RenewableEnergy.Domain.Common;
using RenewableEnergy.Domain.DomainEvents;
using RenewableEnergy.Domain.Enums;
using RenewableEnergy.Domain.ValueObjects;

namespace RenewableEnergy.Domain.Entities
{
    /// <summary>
    /// AGREGADO RAÍZ del dominio. Representa un proyecto de generación de
    /// energía renovable (un parque solar, eólico, una central hidráulica...).
    ///
    /// En Clean Architecture, las entidades de dominio NO conocen EF Core,
    /// DTOs ni nada de la capa de infraestructura. TODA la lógica de negocio
    /// vive aquí: transiciones de estado, invariantes, eventos de dominio.
    ///
    /// Al ser un <see cref="AggregateRoot"/>, es el ÚNICO punto de entrada
    /// para modificar su estado. Las entidades hijas (Medicion, Instalacion)
    /// no se modifican directamente: se hace proyecto.RegistrarMedicion(...),
    /// nunca new Medicion(...) desde un handler.
    ///
    /// FÍJATE EN EL DISEÑO:
    /// - Setters privados: nadie muta el estado desde fuera saltándose reglas.
    /// - Constructor privado + factory Create(): imposible crear un proyecto
    ///   en estado inválido.
    /// - Las colecciones se exponen como IReadOnlyCollection: la lista real
    ///   es privada y solo la entidad la modifica.
    /// </summary>
    public sealed class ProyectoRenovable : AggregateRoot
    {
        private readonly List<Medicion> _mediciones = [];
        private readonly List<Instalacion> _instalaciones = [];

        /// <summary>Nombre comercial del proyecto. Único en el sistema.</summary>
        public string Nombre { get; private set; } = string.Empty;

        /// <summary>Descripción libre, opcional.</summary>
        public string? Descripcion { get; private set; }

        /// <summary>Tecnología de generación (solar, eólica...).</summary>
        public TipoEnergia Tipo { get; private set; }

        /// <summary>Estado dentro del ciclo de vida. Ver <see cref="EstadoProyecto"/>.</summary>
        public EstadoProyecto Estado { get; private set; }

        /// <summary>Potencia instalada total. Value Object: siempre válida.</summary>
        public PotenciaKW Potencia { get; private set; } = null!;

        /// <summary>Ubicación geográfica. Value Object: siempre válida.</summary>
        public Coordenadas Ubicacion { get; private set; } = null!;

        /// <summary>Usuario que creó el proyecto (lo aporta ICurrentUserService).</summary>
        public Guid? PropietarioId { get; private set; }

        /// <summary>Fecha (UTC) en la que el proyecto pasó a Activo.</summary>
        public DateTime? FechaActivacion { get; private set; }

        /// <summary>
        /// REGLA DE NEGOCIO expresada en el dominio: solo se pueden borrar
        /// físicamente los proyectos que nunca llegaron a producir
        /// (Borrador o Cancelado). El resto conserva histórico.
        /// </summary>
        public bool PuedeSerEliminado => Estado is EstadoProyecto.Borrador or EstadoProyecto.Cancelado;

        /// <summary>Mediciones de energía registradas. Solo lectura desde fuera.</summary>
        public IReadOnlyCollection<Medicion> Mediciones => _mediciones.AsReadOnly();

        /// <summary>Instalaciones físicas que componen el proyecto.</summary>
        public IReadOnlyCollection<Instalacion> Instalaciones => _instalaciones.AsReadOnly();

        // Constructor privado SIN parámetros: lo exige EF Core para
        // materializar la entidad desde la base de datos. Al ser privado,
        // el código de aplicación no puede usarlo.
        private ProyectoRenovable()
        {
        }

        /// <summary>
        /// FACTORY METHOD: único modo de crear un proyecto. Garantiza que
        /// nace en estado válido (Borrador) y levanta el evento
        /// <see cref="ProyectoCreado"/>.
        /// </summary>
        /// <exception cref="DomainException">Si el nombre está vacío.</exception>
        public static ProyectoRenovable Create(
            string nombre,
            TipoEnergia tipo,
            PotenciaKW potencia,
            Coordenadas ubicacion,
            Guid? propietarioId = null,
            string? descripcion = null)
        {
            ValidarNombre(nombre);

            var proyecto = new ProyectoRenovable
            {
                Nombre = nombre.Trim(),
                Descripcion = descripcion,
                Tipo = tipo,
                Estado = EstadoProyecto.Borrador,
                Potencia = potencia,
                Ubicacion = ubicacion,
                PropietarioId = propietarioId
            };

            proyecto.RaiseDomainEvent(new ProyectoCreado(
                proyecto.Id,
                proyecto.Nombre,
                proyecto.Tipo,
                DateTime.UtcNow));

            return proyecto;
        }

        /// <summary>
        /// Actualiza los datos editables. REGLA: los proyectos en estado
        /// terminal (Completado/Cancelado) son inmutables.
        /// </summary>
        public void Actualizar(string nombre, string? descripcion, PotenciaKW potencia, Coordenadas ubicacion)
        {
            if (EstadoEsTerminal())
            {
                throw new DomainException(
                    $"No se puede modificar el proyecto '{Nombre}' porque está en estado terminal ({Estado}).");
            }

            ValidarNombre(nombre);

            Nombre = nombre.Trim();
            Descripcion = descripcion;
            Potencia = potencia;
            Ubicacion = ubicacion;
        }

        /// <summary>
        /// Transición Borrador → Activo. A partir de aquí el proyecto admite
        /// mediciones. Levanta <see cref="ProyectoActivado"/>.
        /// </summary>
        /// <exception cref="DomainException">Si no está en Borrador.</exception>
        public void Activar()
        {
            if (Estado != EstadoProyecto.Borrador)
            {
                throw new DomainException(
                    $"Solo se puede activar un proyecto en Borrador. Estado actual: {Estado}.");
            }

            Estado = EstadoProyecto.Activo;
            FechaActivacion = DateTime.UtcNow;

            RaiseDomainEvent(new ProyectoActivado(Id, Nombre, DateTime.UtcNow));
        }

        /// <summary>
        /// Registra una medición de energía generada. Fíjate: la Medicion la
        /// crea EL AGREGADO (no un handler haciendo new), porque las reglas
        /// "solo proyectos activos miden" y "sin fechas futuras" son
        /// invariantes del agregado.
        /// </summary>
        /// <returns>La medición creada (para devolverla en la respuesta).</returns>
        /// <exception cref="DomainException">Si el proyecto no está activo o la fecha es futura.</exception>
        public Medicion RegistrarMedicion(DateTime fechaMedicion, decimal energiaGeneradaKWh)
        {
            if (Estado != EstadoProyecto.Activo)
            {
                throw new DomainException(
                    $"Solo se pueden registrar mediciones en proyectos Activos. Estado actual: {Estado}.");
            }

            if (fechaMedicion > DateTime.UtcNow)
            {
                throw new DomainException("La fecha de la medición no puede ser futura.");
            }

            var medicion = Medicion.Create(Id, fechaMedicion, energiaGeneradaKWh);
            _mediciones.Add(medicion);

            RaiseDomainEvent(new MedicionRegistrada(
                Id,
                medicion.Id,
                medicion.EnergiaGeneradaKWh,
                DateTime.UtcNow));

            return medicion;
        }

        /// <summary>
        /// Añade una instalación física (un tramo de placas, un aerogenerador...).
        /// Solo se permite mientras el proyecto no esté en estado terminal.
        /// </summary>
        public Instalacion AgregarInstalacion(string nombre, PotenciaKW potenciaInstalada)
        {
            if (EstadoEsTerminal())
            {
                throw new DomainException(
                    $"No se pueden añadir instalaciones a un proyecto en estado {Estado}.");
            }

            var instalacion = Instalacion.Create(Id, nombre, potenciaInstalada);
            _instalaciones.Add(instalacion);
            return instalacion;
        }

        /// <summary>Transición Activo → Completado (fin de vida útil).</summary>
        public void Completar()
        {
            if (Estado != EstadoProyecto.Activo)
            {
                throw new DomainException(
                    $"Solo se puede completar un proyecto Activo. Estado actual: {Estado}.");
            }

            Estado = EstadoProyecto.Completado;
        }

        /// <summary>Cancela el proyecto desde cualquier estado no terminal.</summary>
        public void Cancelar()
        {
            if (EstadoEsTerminal())
            {
                throw new DomainException($"El proyecto ya está en estado terminal ({Estado}).");
            }

            Estado = EstadoProyecto.Cancelado;
        }

        private bool EstadoEsTerminal()
        {
            return Estado is EstadoProyecto.Completado or EstadoProyecto.Cancelado;
        }

        private static void ValidarNombre(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                throw new DomainException("El nombre del proyecto es obligatorio.");
            }
        }
    }
}
