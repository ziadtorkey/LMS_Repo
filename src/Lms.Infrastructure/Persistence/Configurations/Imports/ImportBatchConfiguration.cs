using Lms.Domain.Imports;
using Lms.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Imports;

internal sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.ToTable("ImportBatches");
        builder.Property(x => x.OrgUnitPath)
            .HasColumnType("varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci").IsRequired();
        builder.Property(x => x.Kind).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Culture).HasMaxLength(10).IsRequired();
        builder.Property(x => x.FailureCode).HasMaxLength(50);
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasIndex(x => new { x.Kind, x.CreatedAt });
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ImportRowConfiguration : IEntityTypeConfiguration<ImportRow>
{
    public void Configure(EntityTypeBuilder<ImportRow> builder)
    {
        builder.ToTable("ImportRows");
        builder.Property(x => x.OrgUnitPath)
            .HasColumnType("varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci").IsRequired();
        builder.Property(x => x.ValuesJson).HasColumnType("longtext").IsRequired();
        builder.Property(x => x.ErrorsJson).HasColumnType("longtext");
        builder.HasIndex(x => new { x.BatchId, x.RowNumber }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasOne<ImportBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Cascade);
    }
}
