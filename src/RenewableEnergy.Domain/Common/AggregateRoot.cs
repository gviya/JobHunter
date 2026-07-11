namespace RenewableEnergy.Domain.Common
{
    /// <summary>
    /// Marca una entidad como RAÍZ DE AGREGADO (concepto de DDD).
    ///
    /// ¿QUÉ ES UN AGREGADO?
    /// Un grupo de entidades que cambian JUNTAS y de forma consistente.
    /// Ejemplo: ProyectoRenovable + sus Mediciones + sus Instalaciones.
    ///
    /// REGLAS:
    /// 1. Todo cambio del agregado entra por la raíz. Nadie hace
    ///    "medicion.Cambiar(...)" directamente: se hace
    ///    "proyecto.RegistrarMedicion(...)" y la raíz garantiza las invariantes.
    /// 2. Los repositorios SOLO existen para raíces de agregado
    ///    (IProyectoRepository sí; las mediciones se cargan a través del proyecto
    ///    o mediante un repositorio de solo lectura para queries).
    /// 3. Una transacción = un agregado (regla práctica de consistencia).
    ///
    /// Técnicamente hereda todo de <see cref="BaseEntity"/>; su valor es
    /// SEMÁNTICO: comunica la intención y permite que los tests de
    /// arquitectura verifiquen reglas específicas para agregados.
    /// </summary>
    public abstract class AggregateRoot : BaseEntity
    {
    }
}
