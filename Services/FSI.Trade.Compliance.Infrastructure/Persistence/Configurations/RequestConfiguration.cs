using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Request"/> onto <c>TmX_Request</c> (read slice). Slice 14.</summary>
public class RequestConfiguration : IEntityTypeConfiguration<Request>
{
    public void Configure(EntityTypeBuilder<Request> b)
    {
        b.ToTable("TmX_Request", "dbo");
        b.HasKey(x => x.RequestId);
        b.Property(x => x.RequestId)         .HasColumnName("Request_Id");
        b.Property(x => x.TenantId)          .HasColumnName("Tenant_Id");
        b.Property(x => x.RequestTypeCode)   .HasColumnName("Request_Type_Code").HasMaxLength(10);
        b.Property(x => x.EntityStatusCode)  .HasColumnName("Entity_Status_Code").HasMaxLength(10);
        b.Property(x => x.RequestStatusCode) .HasColumnName("Request_Status_Code").HasMaxLength(10);
        b.Property(x => x.EntityId)          .HasColumnName("Entity_Id").HasMaxLength(50);
        b.Property(x => x.EntityJson)        .HasColumnName("Entity_Json");
        b.Property(x => x.CreatedBy)         .HasColumnName("Created_By").HasMaxLength(200);
        b.Property(x => x.CreatedDate)       .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)     .HasColumnName("Last_Updated_By").HasMaxLength(200);
        b.Property(x => x.LastUpdatedDate)   .HasColumnName("Last_Updated_Date");
        b.HasIndex(x => x.EntityId);
    }
}
