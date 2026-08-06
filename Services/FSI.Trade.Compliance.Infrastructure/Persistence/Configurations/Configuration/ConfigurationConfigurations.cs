using FSI.Trade.Compliance.Domain.Entities.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Configuration;

// EF maps for the Configuration Management tables (2026_07_036). Picked up
// automatically by ApplyConfigurationsFromAssembly in ApplicationDbContext.

public class ConfigurationCategoryConfiguration : IEntityTypeConfiguration<ConfigurationCategory>
{
    public void Configure(EntityTypeBuilder<ConfigurationCategory> b)
    {
        b.ToTable("TmX_Configuration_Category", "dbo");
        b.HasKey(x => x.CategoryKey);
        b.Property(x => x.CategoryKey)  .HasColumnName("Category_Key").HasMaxLength(50);
        b.Property(x => x.Title)        .HasColumnName("Title").HasMaxLength(120).IsRequired();
        b.Property(x => x.Description)  .HasColumnName("Description").HasMaxLength(400);
        b.Property(x => x.DisplayOrder) .HasColumnName("Display_Order");
        b.Property(x => x.VerifyKind)   .HasColumnName("Verify_Kind").HasMaxLength(20);
        b.Property(x => x.IsFlagged)    .HasColumnName("Is_Flagged");
        b.Property(x => x.IsLogging)    .HasColumnName("Is_Logging");
    }
}

public class ConfigurationDefinitionConfiguration : IEntityTypeConfiguration<ConfigurationDefinition>
{
    public void Configure(EntityTypeBuilder<ConfigurationDefinition> b)
    {
        b.ToTable("TmX_Configuration_Definition", "dbo");
        b.HasKey(x => x.ConfigurationDefinitionId);
        b.Property(x => x.ConfigurationDefinitionId).HasColumnName("Configuration_Definition_ID");
        b.Property(x => x.ConfigurationKey)  .HasColumnName("Configuration_Key").HasMaxLength(200).IsRequired();
        b.Property(x => x.DisplayName)       .HasColumnName("Display_Name").HasMaxLength(200).IsRequired();
        b.Property(x => x.Description)       .HasColumnName("Description").HasMaxLength(1000);
        b.Property(x => x.CategoryKey)       .HasColumnName("Category_Key").HasMaxLength(50).IsRequired();
        b.Property(x => x.DataType)          .HasColumnName("Data_Type").HasMaxLength(20).IsRequired();
        b.Property(x => x.DefaultValue)      .HasColumnName("Default_Value");
        b.Property(x => x.ValidationRules)   .HasColumnName("Validation_Rules");
        b.Property(x => x.Unit)              .HasColumnName("Unit").HasMaxLength(40);
        b.Property(x => x.IsEditable)        .HasColumnName("Is_Editable");
        b.Property(x => x.IsSensitive)       .HasColumnName("Is_Sensitive");
        b.Property(x => x.RequiresApproval)  .HasColumnName("Requires_Approval");
        b.Property(x => x.AppliesOnRestart)  .HasColumnName("Applies_On_Restart");
        b.Property(x => x.IsActive)          .HasColumnName("Is_Active");
        b.Property(x => x.DisplayOrder)      .HasColumnName("Display_Order");
        b.Property(x => x.CreatedBy)         .HasColumnName("Created_By").HasMaxLength(100);
        b.Property(x => x.CreatedDate)       .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)     .HasColumnName("Last_Updated_By").HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate)   .HasColumnName("Last_Updated_Date");
    }
}

public class ConfigurationValueConfiguration : IEntityTypeConfiguration<ConfigurationValue>
{
    public void Configure(EntityTypeBuilder<ConfigurationValue> b)
    {
        b.ToTable("TmX_Configuration_Value", "dbo");
        b.HasKey(x => x.ConfigurationValueId);
        b.Property(x => x.ConfigurationValueId)     .HasColumnName("Configuration_Value_ID");
        b.Property(x => x.ConfigurationDefinitionId).HasColumnName("Configuration_Definition_ID");
        b.Property(x => x.Value)        .HasColumnName("Configuration_Value");
        b.Property(x => x.BranchId)     .HasColumnName("Branch_ID");
        b.Property(x => x.RoleId)       .HasColumnName("Role_ID");
        b.Property(x => x.Environment)  .HasColumnName("Environment").HasMaxLength(40);
        b.Property(x => x.PendingValue) .HasColumnName("Pending_Value");
        b.Property(x => x.PendingBy)    .HasColumnName("Pending_By").HasMaxLength(100);
        b.Property(x => x.PendingDate)  .HasColumnName("Pending_Date");
        b.Property(x => x.IsActive)     .HasColumnName("Is_Active");
        b.Property(x => x.CreatedBy)       .HasColumnName("Created_By").HasMaxLength(100);
        b.Property(x => x.CreatedDate)     .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)   .HasColumnName("Last_Updated_By").HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate) .HasColumnName("Last_Updated_Date");
    }
}

public class ConfigurationHistoryConfiguration : IEntityTypeConfiguration<ConfigurationHistory>
{
    public void Configure(EntityTypeBuilder<ConfigurationHistory> b)
    {
        b.ToTable("TmX_Configuration_History", "dbo");
        b.HasKey(x => x.ConfigurationHistoryId);
        b.Property(x => x.ConfigurationHistoryId)   .HasColumnName("Configuration_History_ID");
        b.Property(x => x.ConfigurationDefinitionId).HasColumnName("Configuration_Definition_ID");
        b.Property(x => x.ConfigurationValueId)     .HasColumnName("Configuration_Value_ID");
        b.Property(x => x.OldValue)        .HasColumnName("Old_Value");
        b.Property(x => x.NewValue)        .HasColumnName("New_Value");
        b.Property(x => x.ChangeType)      .HasColumnName("Change_Type").HasMaxLength(20).IsRequired();
        b.Property(x => x.ScopeDescriptor) .HasColumnName("Scope_Descriptor").HasMaxLength(200);
        b.Property(x => x.RequestId)       .HasColumnName("Request_ID");
        b.Property(x => x.ChangedBy)       .HasColumnName("Changed_By").HasMaxLength(100);
        b.Property(x => x.ChangedDate)     .HasColumnName("Changed_Date");
    }
}
