using Lms.Domain.Grading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Academic;

internal sealed class GradingScaleConfiguration : IEntityTypeConfiguration<GradingScale>
{
    public void Configure(EntityTypeBuilder<GradingScale> builder)
    {
        builder.ToTable("GradingScales");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        builder.Property(x => x.SeedKey).HasMaxLength(50);
        builder.HasIndex(x => x.DefaultKey).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasIndex(x => new { x.OrgUnitId, x.SeedKey });
    }
}

internal sealed class GradingScaleBandConfiguration : IEntityTypeConfiguration<GradingScaleBand>
{
    public void Configure(EntityTypeBuilder<GradingScaleBand> builder)
    {
        builder.ToTable("GradingScaleBands");
        ScopedChild.Map(builder, x => x.OrgUnitPath, x => x.OrgUnitId);
        builder.Property(x => x.MinPercent).HasPrecision(5, 2);
        builder.Property(x => x.MaxPercent).HasPrecision(5, 2);
        builder.Property(x => x.Points).HasPrecision(5, 2);
        builder.Property(x => x.Code).HasMaxLength(20).IsRequired();
        builder.Property(x => x.LabelAr).HasMaxLength(200);
        builder.Property(x => x.LabelEn).HasMaxLength(200);
        builder.Property(x => x.DescriptorAr).HasMaxLength(500);
        builder.Property(x => x.DescriptorEn).HasMaxLength(500);
        builder.HasOne<GradingScale>().WithMany().HasForeignKey(x => x.ScaleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ScaleId, x.Order }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
    }
}

internal sealed class WeightTemplateConfiguration : IEntityTypeConfiguration<WeightTemplate>
{
    public void Configure(EntityTypeBuilder<WeightTemplate> builder)
    {
        builder.ToTable("WeightTemplates");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
    }
}

internal sealed class WeightTemplateItemConfiguration : IEntityTypeConfiguration<WeightTemplateItem>
{
    public void Configure(EntityTypeBuilder<WeightTemplateItem> builder)
    {
        builder.ToTable("WeightTemplateItems");
        ScopedChild.Map(builder, x => x.OrgUnitPath, x => x.OrgUnitId);
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Weight).HasPrecision(5, 2);
        builder.HasOne<WeightTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TemplateId, x.Order }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
    }
}
