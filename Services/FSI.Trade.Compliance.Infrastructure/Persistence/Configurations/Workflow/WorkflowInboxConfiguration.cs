using FSI.Trade.Compliance.Domain.Entities.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Workflow;

public class WorkflowInboxConfiguration : IEntityTypeConfiguration<WorkflowInbox>
{
    public void Configure(EntityTypeBuilder<WorkflowInbox> b)
    {
        b.ToTable("WorkflowInbox", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id)                .HasColumnName("Id");
        b.Property(x => x.ProcessId)         .HasColumnName("ProcessId");
        // ICBC_DEMO schema: IdentityId is uniqueidentifier (verified in
        // ICBC_DEMO-Schema.sql line 1291). Identical shape under OptimaJet
        // 3.5.0 and v21 — the table layout is the engine's, not the version's.
        b.Property(x => x.IdentityId)        .HasColumnName("IdentityId").IsRequired();

        // AvailableCommands intentionally NOT mapped — column doesn't exist in
        // this deployment's WorkflowInbox table either. The deployed table
        // has only THREE columns: Id, ProcessId, IdentityId. Reads worked
        // before this change because EF Core lets nullable property reads
        // silently return null when a mapped column is absent. Writes break
        // because the generated INSERT lists the unknown column. Slice 5.6
        // started exercising the write path via FillAllUsersBucket — hence
        // the surprise.
        b.Ignore(x => x.AvailableCommands);

        // AddingDate also absent in this deployment. Slice 5 orders by
        // ProcessId; Slice 6 joins to TmX_Transaction.Created_Date for the
        // user-meaningful chronology. If a future deployment adds either
        // column back, replace the Ignore with a Property/HasColumnName map.
        b.Ignore(x => x.AddingDate);

        b.HasIndex(x => x.IdentityId);
        b.HasIndex(x => x.ProcessId);
    }
}
