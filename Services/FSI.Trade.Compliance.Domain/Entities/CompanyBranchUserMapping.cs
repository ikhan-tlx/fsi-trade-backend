namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// READ-MOSTLY projection over <c>TmX_Company_Branch_Users_Mapping</c>. Slice 6
/// uses it to resolve "which branches does this user belong to" so the
/// Transaction list can be scoped accordingly. Effective-date window mirrors
/// the legacy <c>TransactionService.GetList</c> filter
/// (<c>Effective_Start_Date &lt; UTCNow &amp;&amp; UTCNow &lt; Effective_End_Date</c>).
///
/// Schema source: <c>D:\ICBC - Latest\ICBC_DEMO-Schema.sql</c> line 796
/// (<c>CREATE TABLE [dbo].[TmX_Company_Branch_Users_Mapping]</c>).
///
/// User-CRUD (Slice 2) may eventually take a write dependency on this table
/// for "user → branch assignment". For now, Slice 6 only reads.
/// </summary>
public class CompanyBranchUserMapping
{
    public int       Id                  { get; set; }     // Company_Branch_User_Map_ID — surrogate identity
    public int       CompanyBranchId     { get; set; }
    public int       TenantId            { get; set; }
    public string    UserId              { get; set; } = "";
    public DateTime  EffectiveStartDate  { get; set; }
    public DateTime  EffectiveEndDate    { get; set; }
    public bool      ActiveFlag          { get; set; }
    public string?   ReportingBossId     { get; set; }

    public DateTime  CreatedDate         { get; set; }
    public string    CreatedBy           { get; set; } = "";
    public DateTime? LastUpdatedDate     { get; set; }
    public string?   LastUpdatedBy       { get; set; }
}
