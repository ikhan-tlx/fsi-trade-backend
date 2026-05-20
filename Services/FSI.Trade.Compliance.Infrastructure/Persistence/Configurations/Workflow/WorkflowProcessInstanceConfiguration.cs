using FSI.Trade.Compliance.Domain.Entities.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Workflow;

public class WorkflowProcessInstanceConfiguration : IEntityTypeConfiguration<WorkflowProcessInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowProcessInstance> b)
    {
        b.ToTable("WorkflowProcessInstance", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id)                         .HasColumnName("Id");
        b.Property(x => x.SchemeId)                   .HasColumnName("SchemeId");
        b.Property(x => x.StateName)                  .HasColumnName("StateName");
        b.Property(x => x.ActivityName)               .HasColumnName("ActivityName");
        b.Property(x => x.PreviousState)              .HasColumnName("PreviousState");
        b.Property(x => x.PreviousActivity)           .HasColumnName("PreviousActivity");
        b.Property(x => x.PreviousActivityForDirect)  .HasColumnName("PreviousActivityForDirect");
        b.Property(x => x.PreviousActivityForReverse) .HasColumnName("PreviousActivityForReverse");
        b.Property(x => x.PreviousStateForDirect)     .HasColumnName("PreviousStateForDirect");
        b.Property(x => x.PreviousStateForReverse)    .HasColumnName("PreviousStateForReverse");
        b.Property(x => x.ParentProcessId)            .HasColumnName("ParentProcessId");
        b.Property(x => x.RootProcessId)              .HasColumnName("RootProcessId");
        b.Property(x => x.SubprocessName)             .HasColumnName("SubprocessName");
        b.Property(x => x.StartingTransition)         .HasColumnName("StartingTransition");
        b.Property(x => x.CalendarName)               .HasColumnName("CalendarName");
        b.Property(x => x.LastTransitionDate)         .HasColumnName("LastTransitionDate");
        b.Property(x => x.TenantId)                   .HasColumnName("TenantId");

        b.HasIndex(x => x.SchemeId);
    }
}
