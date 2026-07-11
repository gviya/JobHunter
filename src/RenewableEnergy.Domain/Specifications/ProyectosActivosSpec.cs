using System.Linq.Expressions;
using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.Enums;

namespace RenewableEnergy.Domain.Specifications
{
    /// <summary>
    /// Specification concreta: "proyectos en estado Activo".
    ///
    /// El criterio de negocio queda con NOMBRE PROPIO y en un único sitio.
    /// Si mañana "activo" pasara a significar "Activo o Pausado", se cambia
    /// aquí y todos los consumidores quedan actualizados.
    /// </summary>
    public sealed class ProyectosActivosSpec : Specification<ProyectoRenovable>
    {
        public override Expression<Func<ProyectoRenovable, bool>> ToExpression()
        {
            return proyecto => proyecto.Estado == EstadoProyecto.Activo;
        }
    }
}
