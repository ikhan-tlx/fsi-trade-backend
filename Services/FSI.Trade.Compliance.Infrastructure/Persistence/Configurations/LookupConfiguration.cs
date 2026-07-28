using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

public class LookupConfiguration : IEntityTypeConfiguration<Lookup>
{
    public void Configure(EntityTypeBuilder<Lookup> b)
    {
        b.ToTable("TmX_Lookup", "dbo");
        b.HasKey(l => l.Id);

        b.Property(l => l.Id)              .HasColumnName("Lookup_ID");
        b.Property(l => l.ParentLookupId)  .HasColumnName("Parent_Lookup_ID");
        b.Property(l => l.TenantId)        .HasColumnName("Tenant_ID");
        b.Property(l => l.LookupType)      .HasColumnName("Lookup_Type");
        b.Property(l => l.LookupName)      .HasColumnName("Lookup_Name");
        b.Property(l => l.Description)     .HasColumnName("Description");
        b.Property(l => l.VisibleValue)    .HasColumnName("Visible_Value");
        b.Property(l => l.HiddenValue)     .HasColumnName("Hidden_Value");
        b.Property(l => l.IsActive)        .HasColumnName("Is_Active");
        b.Property(l => l.ActiveFlag)      .HasColumnName("Active_Flag");
        // User_Editable is a NULLABLE INT column in this DB (0/1, NULL on legacy rows),
        // but the CLR concept is boolean. Mapping a non-nullable bool threw
        // "Unable to cast Int32 to Boolean", and even the int converter threw
        // SqlNullValueException on the NULL legacy rows. So the property is bool?
        // with an explicit null-safe int? converter: NULL stays NULL (callers treat
        // NULL as "editable"; only an explicit 0 locks a row).
        b.Property(l => l.UserEditable)    .HasColumnName("User_Editable")
            .HasConversion(
                v => v.HasValue ? (v.Value ? 1 : 0) : (int?)null,
                v => v.HasValue ? v.Value != 0 : (bool?)null);
        b.Property(l => l.SortOrder)       .HasColumnName("Sort_Order");
        b.Property(l => l.LocaleLabel)     .HasColumnName("Locale_Label");
        b.Property(l => l.LocaleId)        .HasColumnName("Locale_ID");
        b.Property(l => l.CreatedBy)       .HasColumnName("Created_By");
        b.Property(l => l.LastUpdatedBy)   .HasColumnName("Last_Updated_By");

        // IMPORTANT: TmX_Lookup.Created_Date is an nvarchar column in this DB
        // (legacy, and migration 20260522_001 writes a CONVERT(NVARCHAR(...))
        // date string into it), but these CLR properties are DateTime. Mapping
        // them makes ANY full-entity Lookup load throw "Unable to cast
        // String to DateTime". Lookups are read-only reference data — the app
        // never writes them via EF and never reads these two fields — so leave
        // them UNMAPPED to remove the landmine. (Every query that needs lookup
        // data already projects the specific columns it uses.)
        b.Ignore(l => l.CreatedDate);
        b.Ignore(l => l.LastUpdatedDate);

        // Group-by-LookupType is the primary read path.
        b.HasIndex(l => l.LookupType);
        b.HasIndex(l => new { l.LookupType, l.LocaleLabel });
    }
}
