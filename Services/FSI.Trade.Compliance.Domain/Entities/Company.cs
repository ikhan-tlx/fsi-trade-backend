namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// READ-ONLY projection over <c>TmX_Company</c>. Slice 14 — added so the
/// user form's "Select Company" dropdown has a LOV source (the new BE had no
/// Company controller at all). Minimal surface: id / code / name.
/// </summary>
public class Company
{
    public int     Id   { get; set; }   // Company_Id
    public string? Code { get; set; }   // Company_Code
    public string? Name { get; set; }   // Company_Name
}
