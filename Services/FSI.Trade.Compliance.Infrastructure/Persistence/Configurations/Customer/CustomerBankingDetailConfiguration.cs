using FSI.Trade.Compliance.Domain.Entities.Customer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Configurations.Customer;

public class CustomerBankingDetailConfiguration : IEntityTypeConfiguration<CustomerBankingDetail>
{
    public void Configure(EntityTypeBuilder<CustomerBankingDetail> b)
    {
        b.ToTable("TmX_Customer_Banking_Details", "dbo");
        b.HasKey(x => x.CustomerBankingDetailId);

        b.Property(x => x.CustomerBankingDetailId)         .HasColumnName("Customer_Banking_Details_Id");
        b.Property(x => x.CustomerMasterId)                .HasColumnName("Customer_Master_Id");
        b.Property(x => x.TenantId)                        .HasColumnName("Tenant_Id");
        b.Property(x => x.ActiveFlag)                      .HasColumnName("Active_Flag");
        b.Property(x => x.EffectiveStartDate)              .HasColumnName("Effective_Start_Date");
        b.Property(x => x.EffectiveEndDate)                .HasColumnName("Effective_End_Date");

        b.Property(x => x.BankAccountNumber)               .HasColumnName("Bank_Account_Number").HasMaxLength(100);
        b.Property(x => x.BankCardNumber)                  .HasColumnName("Bank_Card_Number").HasMaxLength(100);
        b.Property(x => x.CardMemberName)                  .HasColumnName("Card_Member_Name").HasMaxLength(150);
        b.Property(x => x.BranchCode)                      .HasColumnName("Branch_Code").HasMaxLength(50);
        b.Property(x => x.ChequeBookNumber)                .HasColumnName("Cheque_Book_Number").HasMaxLength(250);
        b.Property(x => x.AddressTypeLkpId)                .HasColumnName("Address_Type_Lkp_Id");
        b.Property(x => x.InternetBanking)                 .HasColumnName("Internet_Banking");
        b.Property(x => x.InternetBankingTransactionAmount).HasColumnName("Internet_Banking_Transaction_Amount");
        b.Property(x => x.InternetAtmTransactionAmount)    .HasColumnName("Internet_ATM_Transaction_Amount");
        b.Property(x => x.MailingCommunication)            .HasColumnName("Mailing_Communication").HasMaxLength(200);
        b.Property(x => x.UdfData)                         .HasColumnName("UDF_Data");

        b.Property(x => x.CreatedBy)                       .HasColumnName("Created_By").HasMaxLength(150).IsRequired();
        b.Property(x => x.CreatedDate)                     .HasColumnName("Created_Date");
        b.Property(x => x.LastUpdatedBy)                   .HasColumnName("Last_Updated_By").HasMaxLength(150);
        b.Property(x => x.LastUpdatedDate)                 .HasColumnName("Last_Updated_Date");

        b.HasIndex(x => x.CustomerMasterId);
    }
}
