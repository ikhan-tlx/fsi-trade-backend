using FSI.Trade.Compliance.Domain.Entities.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Workflow;

/// <summary>
/// Slice 10 — read-only mapping of OptimaJet's transition history table.
/// Column names + types verified against ICBC_DEMO-Schema.sql.
/// </summary>
public class WorkflowProcessTransitionHistoryConfiguration
    : IEntityTypeConfiguration<WorkflowProcessTransitionHistory>
{
    public void Configure(EntityTypeBuilder<WorkflowProcessTransitionHistory> b)
    {
        b.ToTable("WorkflowProcessTransitionHistory", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id)                  .HasColumnName("Id");
        b.Property(x => x.ProcessId)           .HasColumnName("ProcessId");
        b.Property(x => x.ExecutorIdentityId)  .HasColumnName("ExecutorIdentityId").HasMaxLength(256);
        b.Property(x => x.ActorIdentityId)     .HasColumnName("ActorIdentityId").HasMaxLength(256);
        b.Property(x => x.FromActivityName)    .HasColumnName("FromActivityName").IsRequired();
        b.Property(x => x.ToActivityName)      .HasColumnName("ToActivityName").IsRequired();
        b.Property(x => x.FromStateName)       .HasColumnName("FromStateName");
        b.Property(x => x.ToStateName)         .HasColumnName("ToStateName");
        b.Property(x => x.TransitionTime)      .HasColumnName("TransitionTime");
        b.Property(x => x.TransitionClassifier).HasColumnName("TransitionClassifier").IsRequired();
        b.Property(x => x.IsFinalised)         .HasColumnName("IsFinalised");
        b.Property(x => x.TriggerName)         .HasColumnName("TriggerName");

        b.HasIndex(x => x.ProcessId);
    }
}
