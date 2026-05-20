using FSI.Trade.Compliance.Domain.Entities.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Application;

public class ApplicationDeviationViewConfiguration : IEntityTypeConfiguration<ApplicationDeviationView>
{
    public void Configure(EntityTypeBuilder<ApplicationDeviationView> b)
    {
        b.ToView("TmX_Application_Deviation_VW", "dbo");
        b.HasNoKey();

        b.Property(x => x.Creator)         .HasColumnName("Creator");
        b.Property(x => x.RuleName)        .HasColumnName("Rule_Name");
        b.Property(x => x.RuleMessage)     .HasColumnName("Rule_Message");
        b.Property(x => x.DeviationAction) .HasColumnName("Deviation_Action");
        b.Property(x => x.UserId)          .HasColumnName("User_ID");
        b.Property(x => x.TransactionId)   .HasColumnName("Transaction_ID");
        b.Property(x => x.ModuleCode)      .HasColumnName("Module_Code");
        b.Property(x => x.ApprovalId)      .HasColumnName("Approval_ID");
        b.Property(x => x.DeviationId)     .HasColumnName("Deviation_ID");
    }
}
