using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations;

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.ToTable("TmX_User", "dbo");
        b.HasKey(u => u.Id);

        b.Property(u => u.Id)                       .HasColumnName("User_ID").IsRequired();
        b.Property(u => u.UserName)                 .HasColumnName("User_Name");
        b.Property(u => u.Email)                    .HasColumnName("Email_Address");
        b.Property(u => u.PhoneNumber)              .HasColumnName("Contact_Number");
        b.Property(u => u.FirstName)                .HasColumnName("First_Name");
        b.Property(u => u.MiddleName)               .HasColumnName("Middle_Name");
        b.Property(u => u.LastName)                 .HasColumnName("Last_Name");
        b.Property(u => u.ImageURL)                 .HasColumnName("User_Image_URL");
        b.Property(u => u.TenantId)                 .HasColumnName("Tenant_ID");
        b.Property(u => u.LocationId)               .HasColumnName("Location_ID");
        b.Property(u => u.UserTypeLkpId)            .HasColumnName("User_Type_Lkp_ID");
        b.Property(u => u.DesignationLkpId)         .HasColumnName("Designation_Lkp_ID");

        // auth columns from migration 2026_05_001
        b.Property(u => u.PasswordHash)             .HasColumnName("PasswordHash");
        b.Property(u => u.SecurityStamp)            .HasColumnName("SecurityStamp");
        b.Property(u => u.EmailConfirmed)           .HasColumnName("EmailConfirmed");
        b.Property(u => u.PhoneNumberConfirmed)     .HasColumnName("PhoneNumberConfirmed");
        b.Property(u => u.TwoFactorEnabled)         .HasColumnName("TwoFactorEnabled");
        b.Property(u => u.TwoFactorAuthenticatorKey).HasColumnName("TwoFASecretKey");
        b.Property(u => u.LockoutEndDateUtc)        .HasColumnName("LockoutEndDateUtc");
        b.Property(u => u.LockoutEnabled)           .HasColumnName("LockoutEnabled");
        b.Property(u => u.AccessFailedCount)        .HasColumnName("AccessFailedCount");
        b.Property(u => u.Status)                   .HasColumnName("Status");
        b.Property(u => u.LastLoginDate)            .HasColumnName("LastLoginDate");
        b.Property(u => u.PasswordExpiryDate)       .HasColumnName("PasswordExpiryDate");
        b.Property(u => u.FirstPasswordChange)      .HasColumnName("FirstPasswordChange");

        // lifecycle / audit
        b.Property(u => u.ActiveFlag)               .HasColumnName("Active_Flag");
        b.Property(u => u.EffectiveStartDate)       .HasColumnName("Effective_Start_Date");
        b.Property(u => u.EffectiveEndDate)         .HasColumnName("Effective_End_Date");
        b.Property(u => u.CreatedBy)                .HasColumnName("Created_By");
        b.Property(u => u.CreatedDate)              .HasColumnName("Created_Date");
        b.Property(u => u.LastUpdatedBy)            .HasColumnName("Last_Updated_By");
        b.Property(u => u.LastUpdatedDate)          .HasColumnName("Last_Updated_Date");
    }
}
