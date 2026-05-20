using FSI.Trade.Compliance.Domain.Entities.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Application;

public class ApplicationRemarkConfiguration : IEntityTypeConfiguration<ApplicationRemark>
{
    public void Configure(EntityTypeBuilder<ApplicationRemark> b)
    {
        b.ToTable("TmX_Application_Remark", "dbo");
        b.HasKey(x => x.ApplicationRemarkId);

        b.Property(x => x.ApplicationRemarkId) .HasColumnName("Application_Remark_ID");
        b.Property(x => x.TransactionId)       .HasColumnName("Transaction_ID");
        b.Property(x => x.ModuleCode)          .HasColumnName("Module_Code").HasMaxLength(10).IsRequired();
        b.Property(x => x.TenantId)            .HasColumnName("Tenant_ID");
        b.Property(x => x.ActionType)          .HasColumnName("Action_Type").HasMaxLength(30);
        b.Property(x => x.RemarksLkp)          .HasColumnName("Remarks_Lkp");
        b.Property(x => x.Comments)            .HasColumnName("Comments");
        b.Property(x => x.UserId)              .HasColumnName("User_ID").HasMaxLength(50).IsRequired();
        b.Property(x => x.MobileId)            .HasColumnName("Mobile_ID");

        b.Property(x => x.CreatedBy)           .HasColumnName("Created_By").HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedDate)         .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)       .HasColumnName("Last_Updated_By").HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate)     .HasColumnName("Last_Updated_Date");

        b.HasIndex(x => new { x.TransactionId, x.ModuleCode });
    }
}
