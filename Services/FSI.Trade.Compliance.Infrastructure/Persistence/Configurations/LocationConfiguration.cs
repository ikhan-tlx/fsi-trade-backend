using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Location"/> onto <c>TmX_Location</c>. Read-only from the
/// new backend; the hierarchy walker in <c>LocationHierarchyService</c>
/// loads every active row in one pass and recurses in memory.
///
/// Schema source: <c>D:\ICBC - Latest\ICBC_DEMO-Schema.sql</c> line 714.
/// </summary>
public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> b)
    {
        b.ToTable("TmX_Location", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id)                 .HasColumnName("Location_ID");
        b.Property(x => x.ParentLocationId)   .HasColumnName("Parent_Location_ID");
        b.Property(x => x.TenantId)           .HasColumnName("Tenant_ID");
        b.Property(x => x.LocationCode)       .HasColumnName("Location_Code").HasMaxLength(50).IsRequired();
        b.Property(x => x.LocationName)       .HasColumnName("Location_Name").HasMaxLength(200);
        b.Property(x => x.ActiveFlag)         .HasColumnName("Active_Flag");
        b.Property(x => x.EffectiveStartDate) .HasColumnName("Effective_Start_Date");
        b.Property(x => x.EffectiveEndDate)   .HasColumnName("Effective_End_Date");
        b.Property(x => x.LocationTypeLkpId)  .HasColumnName("Location_Type_Lkp_ID");

        b.HasIndex(x => x.ParentLocationId);
    }
}
