using JobHunter.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Configurations;

public sealed class DailyReportConfiguration : IEntityTypeConfiguration<DailyReport>
{
    public void Configure(EntityTypeBuilder<DailyReport> builder)
    {
        builder.HasKey(r => r.Id);

        // TopOffers es material de trabajo en memoria: no se persiste.
        builder.Ignore(r => r.TopOffers);

        builder.Property(r => r.HtmlContent);
    }
}
