using FSI.Trade.Compliance.Domain.Entities.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Workflow;

/// <summary>
/// Maps the read-only projection over OptimaJet's <c>WorkflowProcessScheme</c>.
/// If your live schema uses a different column name for SchemeCode
/// (e.g. legacy v3.x had <c>Code</c>), update the HasColumnName below — the
/// entity property stays.
/// </summary>
public class WorkflowProcessSchemeConfiguration : IEntityTypeConfiguration<WorkflowProcessScheme>
{
    public void Configure(EntityTypeBuilder<WorkflowProcessScheme> b)
    {
        b.ToTable("WorkflowProcessScheme", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id)                     .HasColumnName("Id");
        b.Property(x => x.SchemeCode)             .HasColumnName("SchemeCode").IsRequired();
        b.Property(x => x.Scheme)                 .HasColumnName("Scheme");
        b.Property(x => x.AllowedActivities)      .HasColumnName("AllowedActivities");
        b.Property(x => x.StartingTransition)     .HasColumnName("StartingTransition");
        b.Property(x => x.DefiningParameters)     .HasColumnName("DefiningParameters");
        b.Property(x => x.IsObsolete)             .HasColumnName("IsObsolete");
        b.Property(x => x.RootSchemeId)           .HasColumnName("RootSchemeId");
        b.Property(x => x.RootSchemeCode)         .HasColumnName("RootSchemeCode");
        b.Property(x => x.DefiningParametersHash) .HasColumnName("DefiningParametersHash");

        b.HasIndex(x => x.SchemeCode);
    }
}
