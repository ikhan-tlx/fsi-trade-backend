using FSI.Trade.Compliance.Domain.Entities.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Reports;

public class TemplateConfiguration : IEntityTypeConfiguration<Template>
{
    public void Configure(EntityTypeBuilder<Template> b)
    {
        b.ToTable("TmX_Template", "dbo");
        b.HasKey(x => x.TemplateId);

        b.Property(x => x.TemplateId)          .HasColumnName("Template_ID");
        b.Property(x => x.TemplateName)        .HasColumnName("Template_Name").HasMaxLength(100).IsRequired();
        b.Property(x => x.TemplateDescription) .HasColumnName("Template_Description").HasMaxLength(100);
        b.Property(x => x.TemplateText)        .HasColumnName("Template_Text");
        b.Property(x => x.TemplateTypeLkpId)   .HasColumnName("Template_Type_Lkp_ID");
        b.Property(x => x.TenantId)            .HasColumnName("Tenant_ID");
        b.Property(x => x.ProductId)           .HasColumnName("Product_Id");
        b.Property(x => x.IsProtected)         .HasColumnName("Is_Protected");
        b.Property(x => x.PasswordBinding)     .HasColumnName("Password_Binding").HasMaxLength(250);

        b.Property(x => x.CreatedBy)           .HasColumnName("Created_By").HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedDate)         .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)       .HasColumnName("Last_Updated_By").HasMaxLength(100);
        b.Property(x => x.LastUpdatedDate)     .HasColumnName("Last_Updated_Date");

        b.HasIndex(x => x.TemplateName);
    }
}
