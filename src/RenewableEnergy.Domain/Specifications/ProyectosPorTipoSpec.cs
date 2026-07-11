using System.Linq.Expressions;
using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.Enums;

namespace RenewableEnergy.Domain.Specifications
{
    /// <summary>
    /// Specification PARAMETRIZADA: "proyectos de un tipo de energía concreto".
    ///
    /// Ejemplo de combinación (así se usa en ObtenerProyectosHandler):
    /// <code>
    /// Specification&lt;ProyectoRenovable&gt; spec = new ProyectosPorTipoSpec(TipoEnergia.Solar);
    /// spec = spec.And(new ProyectosActivosSpec()); // solares Y activos
    /// </code>
    /// </summary>
    public sealed class ProyectosPorTipoSpec(TipoEnergia tipo) : Specification<ProyectoRenovable>
    {
        public override Expression<Func<ProyectoRenovable, bool>> ToExpression()
        {
            return proyecto => proyecto.Tipo == tipo;
        }
    }
}
