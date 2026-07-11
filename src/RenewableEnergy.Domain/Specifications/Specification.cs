using System.Linq.Expressions;

namespace RenewableEnergy.Domain.Specifications
{
    /// <summary>
    /// Clase base del PATRÓN SPECIFICATION.
    ///
    /// ¿QUÉ PROBLEMA RESUELVE?
    /// Sin este patrón, los criterios de consulta ("proyectos activos",
    /// "proyectos solares") acaban duplicados en lambdas repartidas por
    /// handlers y repositorios. Una Specification encapsula UN criterio de
    /// negocio con nombre propio, testeable de forma aislada y REUTILIZABLE.
    ///
    /// ¿POR QUÉ ES COMBINABLE?
    /// Los métodos <see cref="And"/>, <see cref="Or"/> y <see cref="Not"/>
    /// permiten componer criterios como si fueran álgebra booleana:
    /// <code>
    /// var spec = new ProyectosActivosSpec().And(new ProyectosPorTipoSpec(TipoEnergia.Solar));
    /// var proyectos = await repository.GetBySpecificationAsync(spec);
    /// </code>
    ///
    /// ¿CÓMO SE EJECUTA?
    /// <see cref="ToExpression"/> devuelve un árbol de expresión que el
    /// repositorio de Infrastructure pasa a EF Core (se traduce a SQL, no se
    /// filtra en memoria). <see cref="IsSatisfiedBy"/> permite evaluar el
    /// criterio sobre un objeto en memoria (útil en tests y validaciones).
    /// </summary>
    public abstract class Specification<T>
    {
        /// <summary>Expresión que define el criterio. La traduce EF Core a SQL.</summary>
        public abstract Expression<Func<T, bool>> ToExpression();

        /// <summary>Evalúa el criterio contra un objeto en memoria.</summary>
        public bool IsSatisfiedBy(T candidato)
        {
            return ToExpression().Compile()(candidato);
        }

        /// <summary>Combina dos criterios con AND lógico.</summary>
        public Specification<T> And(Specification<T> otra)
        {
            return new AndSpecification<T>(this, otra);
        }

        /// <summary>Combina dos criterios con OR lógico.</summary>
        public Specification<T> Or(Specification<T> otra)
        {
            return new OrSpecification<T>(this, otra);
        }

        /// <summary>Niega el criterio actual.</summary>
        public Specification<T> Not()
        {
            return new NotSpecification<T>(this);
        }
    }

    /// <summary>
    /// Specification compuesta: izquierda AND derecha. Es interna porque el
    /// consumidor no la instancia: usa el método fluido <c>spec.And(otra)</c>.
    /// </summary>
    internal sealed class AndSpecification<T>(Specification<T> izquierda, Specification<T> derecha) : Specification<T>
    {
        public override Expression<Func<T, bool>> ToExpression()
        {
            return CombinadorExpresiones.Combinar(izquierda.ToExpression(), derecha.ToExpression(), Expression.AndAlso);
        }
    }

    /// <summary>Specification compuesta: izquierda OR derecha.</summary>
    internal sealed class OrSpecification<T>(Specification<T> izquierda, Specification<T> derecha) : Specification<T>
    {
        public override Expression<Func<T, bool>> ToExpression()
        {
            return CombinadorExpresiones.Combinar(izquierda.ToExpression(), derecha.ToExpression(), Expression.OrElse);
        }
    }

    /// <summary>Specification compuesta: NOT(interna).</summary>
    internal sealed class NotSpecification<T>(Specification<T> interna) : Specification<T>
    {
        public override Expression<Func<T, bool>> ToExpression()
        {
            var expresion = interna.ToExpression();
            var negada = Expression.Not(expresion.Body);
            return Expression.Lambda<Func<T, bool>>(negada, expresion.Parameters);
        }
    }

    /// <summary>
    /// Utilidad para fusionar dos árboles de expresión en uno solo.
    ///
    /// DETALLE TÉCNICO (el "truco" del patrón): cada lambda tiene SU PROPIO
    /// parámetro (x => ..., y => ...). Para combinarlas en una sola expresión
    /// hay que reescribir el cuerpo de la segunda sustituyendo su parámetro
    /// por el de la primera. De eso se encarga el <see cref="ReemplazarParametroVisitor"/>.
    /// Si no se hiciera, EF Core lanzaría "parameter was not bound".
    /// </summary>
    internal static class CombinadorExpresiones
    {
        internal static Expression<Func<T, bool>> Combinar<T>(
            Expression<Func<T, bool>> izquierda,
            Expression<Func<T, bool>> derecha,
            Func<Expression, Expression, BinaryExpression> operador)
        {
            var parametro = izquierda.Parameters[0];
            var cuerpoDerechaReescrito = new ReemplazarParametroVisitor(derecha.Parameters[0], parametro)
                .Visit(derecha.Body);

            var cuerpoCombinado = operador(izquierda.Body, cuerpoDerechaReescrito);
            return Expression.Lambda<Func<T, bool>>(cuerpoCombinado, parametro);
        }

        private sealed class ReemplazarParametroVisitor(ParameterExpression original, ParameterExpression sustituto)
            : ExpressionVisitor
        {
            protected override Expression VisitParameter(ParameterExpression node)
            {
                return node == original ? sustituto : base.VisitParameter(node);
            }
        }
    }
}
