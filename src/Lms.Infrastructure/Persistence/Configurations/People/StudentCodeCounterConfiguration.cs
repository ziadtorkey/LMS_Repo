using Lms.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.People;

internal sealed class StudentCodeCounterConfiguration : IEntityTypeConfiguration<StudentCodeCounter>
{
    public void Configure(EntityTypeBuilder<StudentCodeCounter> builder)
    {
        builder.ToTable("StudentCodeCounters");
        builder.HasKey(x => x.OrganizationId);
        builder.Property(x => x.OrganizationId).ValueGeneratedNever();
        builder.Property(x => x.NextValue).HasColumnType("bigint");
    }
}
