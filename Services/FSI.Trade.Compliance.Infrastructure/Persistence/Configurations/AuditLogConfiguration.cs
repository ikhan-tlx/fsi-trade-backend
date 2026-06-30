using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("TmX_Audit_Log", "dbo");
        b.HasKey(x => x.AuditId);
        b.Property(x => x.AuditId)     .HasColumnName("Audit_Id");
        b.Property(x => x.TenantId)    .HasColumnName("Tenant_Id");
        b.Property(x => x.UserId)      .HasColumnName("User_Id").HasMaxLength(100);
        b.Property(x => x.UserName)    .HasColumnName("User_Name").HasMaxLength(200);
        b.Property(x => x.IpAddress)   .HasColumnName("Ip_Address").HasMaxLength(64);
        b.Property(x => x.Action)      .HasColumnName("Action").HasMaxLength(20);
        b.Property(x => x.UserRole)    .HasColumnName("User_Role").HasMaxLength(400);
        b.Property(x => x.OnBehalfOf)  .HasColumnName("On_Behalf_Of").HasMaxLength(200);
        b.Property(x => x.EntityType)  .HasColumnName("Entity_Type").HasMaxLength(200);
        b.Property(x => x.EntityKey)   .HasColumnName("Entity_Key").HasMaxLength(200);
        b.Property(x => x.ChangesJson) .HasColumnName("Changes_Json");
        b.Property(x => x.Description) .HasColumnName("Description").HasMaxLength(1000);
        b.Property(x => x.Screen)      .HasColumnName("Screen").HasMaxLength(200);
        b.Property(x => x.RequestPath) .HasColumnName("Request_Path").HasMaxLength(400);
        b.Property(x => x.HttpMethod)  .HasColumnName("Http_Method").HasMaxLength(10);
        b.Property(x => x.StatusCode)  .HasColumnName("Status_Code");
        b.Property(x => x.Outcome)     .HasColumnName("Outcome").HasMaxLength(20);
        b.Property(x => x.FailureReason).HasColumnName("Failure_Reason").HasMaxLength(1000);
        b.Property(x => x.Host)        .HasColumnName("Host").HasMaxLength(200);
        b.Property(x => x.Application) .HasColumnName("Application").HasMaxLength(200);
        b.Property(x => x.SessionId)   .HasColumnName("Session_Id").HasMaxLength(100);
        b.Property(x => x.CorrelationId).HasColumnName("Correlation_Id").HasMaxLength(100);
        b.Property(x => x.UserAgent)   .HasColumnName("User_Agent").HasMaxLength(512);
        b.Property(x => x.RowHash)     .HasColumnName("Row_Hash").HasMaxLength(64);
        b.Property(x => x.PrevHash)    .HasColumnName("Prev_Hash").HasMaxLength(64);
        b.Property(x => x.CreatedDate) .HasColumnName("Created_Date");
    }
}
