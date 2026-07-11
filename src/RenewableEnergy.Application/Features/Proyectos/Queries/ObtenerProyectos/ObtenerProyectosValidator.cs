using FluentValidation;

namespace RenewableEnergy.Application.Features.Proyectos.Queries.ObtenerProyectos
{
    /// <summary>
    /// Las QUERIES también se validan: una página negativa o un tamaño de
    /// 100.000 filas deben rechazarse ANTES de llegar a la base de datos.
    /// </summary>
    public sealed class ObtenerProyectosValidator : AbstractValidator<ObtenerProyectosQuery>
    {
        public ObtenerProyectosValidator()
        {
            RuleFor(q => q.Pagina)
                .GreaterThanOrEqualTo(1).WithMessage("La página debe ser mayor o igual que 1.");

            RuleFor(q => q.TamanoPagina)
                .InclusiveBetween(1, 100).WithMessage("El tamaño de página debe estar entre 1 y 100.");

            RuleFor(q => q.Tipo)
                .IsInEnum().When(q => q.Tipo.HasValue)
                .WithMessage("El tipo de energía no es válido.");
        }
    }
}
