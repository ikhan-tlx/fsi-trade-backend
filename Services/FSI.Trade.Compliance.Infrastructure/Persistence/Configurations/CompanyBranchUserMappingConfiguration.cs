using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="CompanyBranchUserMapping"/> onto
/// <c>TmX_Company_Branch_Users_Mapping</c>. Read-mostly today (Slice 6 uses
/// it for branch scoping); writes will land when User-CRUD's branch
/// assignment story is filled in.
///
/// Schema source: <c>D:\ICBC - Latest\ICBC_DEMO-Schema.sql</c> line 796.
/// </summary>
public class CompanyBranchUserMappingConfiguration : IEntityTypeConfiguration<CompanyBranchUserMapping>
{
    public void Configure(EntityTypeBuilder<CompanyBranchUserMapping> b)
    {
        b.ToTable("TmX_Company_Branch_Users_Mapping", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id)                 .HasColumnName("Company_Branch_User_Map_ID");
        b.Property(x => x.CompanyBranchId)    .HasColumnName("Company_Branch_ID");
        b.Property(x => x.TenantId)           .HasColumnName("Tenant_ID");
        b.Property(x => x.UserId)             .HasColumnName("User_Id").HasMaxLength(50).IsRequired();
        b.Property(x => x.EffectiveStartDate) .HasColumnName("Effective_Start_Date");
        b.Property(x => x.EffectiveEndDate)   .HasColumnName("Effective_End_Date");
        b.Property(x => x.ActiveFlag)         .HasColumnName("Active_Flag");
        b.Property(x => x.ReportingBossId)    .HasColumnName("Reporting_Boss_Id").HasMaxLength(50);
        b.Property(x => x.CreatedDate)        .HasColumnName("Created_Date");
        b.Property(x => x.CreatedBy)          .HasColumnName("Created_By").HasMaxLength(100).IsRequired();
        b.Property(x => x.LastUpdatedDate)    .HasColumnName("Last_Updated_Date");
        b.Property(x => x.LastUpdatedBy)      .HasColumnName("Last_Updated_By").HasMaxLength(100);

        // "Branches for user" is the hot read path.
        b.HasIndex(x => x.UserId);
        b.HasIndex(x => x.CompanyBranchId);
    }
}
