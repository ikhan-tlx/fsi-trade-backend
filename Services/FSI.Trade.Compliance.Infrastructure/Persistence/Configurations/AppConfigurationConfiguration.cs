using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

public class AppConfigurationConfiguration : IEntityTypeConfiguration<AppConfiguration>
{
    public void Configure(EntityTypeBuilder<AppConfiguration> b)
    {
        b.ToTable("TmX_Configuration", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id)                       .HasColumnName("Configuration_ID");
        b.Property(x => x.TenantId)                 .HasColumnName("Tenant_ID");
        b.Property(x => x.ConfigurationKey)         .HasColumnName("Configuration_Key");
        b.Property(x => x.ConfigurationValue)       .HasColumnName("Configuration_Value");
        b.Property(x => x.ConfigurationDescription) .HasColumnName("Configuration_Description");
        b.Property(x => x.ConfigurationTypeLkpId)   .HasColumnName("Configuration_Type_Lkp");
        b.Property(x => x.ConfigurationStatusLkpId) .HasColumnName("Configuration_Status_Lkp_ID");
        b.Property(x => x.EffectiveStartDate)       .HasColumnName("Effective_Start_Date");
        b.Property(x => x.EffectiveEndDate)         .HasColumnName("Effective_End_Date");
        b.Property(x => x.TimeZoneId)               .HasColumnName("Time_Zone_ID");
        b.Property(x => x.ProductId)                .HasColumnName("Product_Id");
        b.Property(x => x.CreatedBy)                .HasColumnName("Created_By");
        b.Property(x => x.CreatedDate)              .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)            .HasColumnName("Last_Updated_By");
        b.Property(x => x.LastUpdatedDate)          .HasColumnName("Last_Updated_Date");

        b.HasIndex(x => new { x.TenantId, x.ConfigurationKey });
    }
}
