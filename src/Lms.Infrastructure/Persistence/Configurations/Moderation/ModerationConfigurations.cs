using Lms.Domain.Moderation;
using Lms.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Moderation;

internal sealed class MessageReportConfiguration : IEntityTypeConfiguration<MessageReport>
{
    public void Configure(EntityTypeBuilder<MessageReport> builder)
    {
        builder.ToTable("MessageReports");
        builder.Property(x => x.Reason).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.Action).HasMaxLength(30);
        builder.Property(x => x.ContextJson).HasColumnType("longtext").IsRequired();
        builder.Property(x => x.ReportKey).HasMaxLength(70);
        builder.HasIndex(x => x.ReportKey).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.Status, x.CreatedAt });
        builder.HasIndex(x => new { x.OrganizationId, x.ReportedUserId });
        builder.HasIndex(x => new { x.ReporterUserId, x.CreatedAt });
        builder.HasIndex(x => x.MessageId);
    }
}

internal sealed class MessageReportUnitConfiguration : IEntityTypeConfiguration<MessageReportUnit>
{
    public void Configure(EntityTypeBuilder<MessageReportUnit> builder)
    {
        builder.ToTable("MessageReportUnits");
        builder.Property(x => x.OrgUnitPath).HasColumnType("varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci").IsRequired();
        builder.HasIndex(x => x.ReportId);
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MessageReport>().WithMany().HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Restrict);
    }
}
