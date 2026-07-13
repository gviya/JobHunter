using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;
using MediatR;

namespace JobHunter.Application.Features.Reports;

/// <summary>Devuelve el último informe diario guardado (o null si no hay).</summary>
public static class GetLatestReport
{
    public sealed record Query : IRequest<DailyReport?>;

    internal sealed class Handler(IJobRepository repository) : IRequestHandler<Query, DailyReport?>
    {
        public Task<DailyReport?> Handle(Query request, CancellationToken cancellationToken)
            => repository.GetLatestReportAsync(cancellationToken);
    }
}
