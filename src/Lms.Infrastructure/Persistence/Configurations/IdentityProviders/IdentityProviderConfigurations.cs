using Lms.Domain.IdentityProviders;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Identity.External;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.IdentityProviders;

internal sealed class IdentityProviderConfigConfiguration : IEntityTypeConfiguration<IdentityProviderConfig>
{
    public void Configure(EntityTypeBuilder<IdentityProviderConfig> builder)
    {
        builder.ToTable("IdentityProviders");
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.MatchBy).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Slug).HasColumnType("varchar(40) CHARACTER SET ascii COLLATE ascii_general_ci").IsRequired();
        builder.Property(x => x.DisplayNameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DisplayNameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Authority).HasMaxLength(500).IsRequired();
        builder.Property(x => x.AzureTenantId).HasMaxLength(100);
        builder.Property(x => x.ClientId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ProtectedClientSecret).HasMaxLength(2000);
        builder.Property(x => x.Scopes).HasMaxLength(500).IsRequired();
        builder.Property(x => x.NationalIdClaim).HasMaxLength(100);
        builder.Property(x => x.EmailDomains).HasMaxLength(1000).IsRequired();

        // Unique across ALL clients (the callback path /signin-oidc/{slug} carries no client), soft-deleted rows included.
        builder.HasIndex(x => x.Slug).IsUnique();
        builder.HasIndex(x => x.OrganizationId);
        builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DataProtectionKeyConfiguration : IEntityTypeConfiguration<DataProtectionKey>
{
    public void Configure(EntityTypeBuilder<DataProtectionKey> builder)
    {
        builder.ToTable("DataProtectionKeys");
        builder.Property(x => x.FriendlyName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Xml).HasColumnType("longtext").IsRequired();
    }
}
