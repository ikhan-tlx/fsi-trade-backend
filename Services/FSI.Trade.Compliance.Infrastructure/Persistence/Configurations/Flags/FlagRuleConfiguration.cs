using FSI.Trade.Compliance.Domain.Entities.Flags;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Flags;

public class FlagRuleConfiguration : IEntityTypeConfiguration<FlagRule>
{
    public void Configure(EntityTypeBuilder<FlagRule> b)
    {
        b.ToTable("TmX_Flag_Rule", "dbo");
        b.HasKey(x => x.RuleId);

        b.Property(x => x.RuleId)             .HasColumnName("Rule_ID");
        b.Property(x => x.FlagId)             .HasColumnName("Flag_ID");
        b.Property(x => x.RuleGroup)          .HasColumnName("Rule_Group");
        b.Property(x => x.EvaluationOrder)    .HasColumnName("Evaluation_Order");
        b.Property(x => x.LogicalOperator)    .HasColumnName("Logical_Operator")    .HasMaxLength(3).IsRequired();
        b.Property(x => x.LeftSource)         .HasColumnName("Left_Source")         .HasMaxLength(20).IsRequired();
        b.Property(x => x.LeftFieldCode)      .HasColumnName("Left_Field_Code")     .HasMaxLength(100);
        b.Property(x => x.Operator)           .HasColumnName("Operator")            .HasMaxLength(40).IsRequired();
        b.Property(x => x.RightOperandType)   .HasColumnName("Right_Operand_Type")  .HasMaxLength(20).IsRequired();
        b.Property(x => x.RightSource)        .HasColumnName("Right_Source")        .HasMaxLength(20);
        b.Property(x => x.RightFieldCode)     .HasColumnName("Right_Field_Code")    .HasMaxLength(100);
        b.Property(x => x.StaticValue)        .HasColumnName("Static_Value")        .HasMaxLength(400);
        b.Property(x => x.ReferenceListCode)  .HasColumnName("Reference_List_Code") .HasMaxLength(60);
        b.Property(x => x.RightFactor)        .HasColumnName("Right_Factor")        .HasColumnType("decimal(18,4)");
        b.Property(x => x.RightFactorUnit)    .HasColumnName("Right_Factor_Unit")   .HasMaxLength(12);
        b.Property(x => x.AggregateSpec)      .HasColumnName("Aggregate_Spec");
        b.Property(x => x.HandlerKey)         .HasColumnName("Handler_Key")         .HasMaxLength(60);
        b.Property(x => x.ActiveFlag)         .HasColumnName("Active_Flag");
        b.Property(x => x.CreatedBy)          .HasColumnName("Created_By")          .HasMaxLength(100);
        b.Property(x => x.CreatedDate)        .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)      .HasColumnName("Last_Updated_By")     .HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate)    .HasColumnName("Last_Updated_Date");

        b.HasIndex(x => new { x.FlagId, x.ActiveFlag });
    }
}
