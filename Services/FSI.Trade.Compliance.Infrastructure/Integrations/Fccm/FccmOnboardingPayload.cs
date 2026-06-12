namespace FSI.Trade.Compliance.Infrastructure.Integrations.Fccm;

/// <summary>
/// FCCM KYC onboarding request body for
/// <c>InitiateOnboardingService/OB/Initiate</c>. Mirrors the legacy
/// <c>KYCRequestModel</c> (ICBC_Integration_Test) so the upstream contract is
/// unchanged. Property names are the EXACT wire keys (PascalCase incl.
/// <c>OnboardingCustomer</c>, <c>ApplicantID</c>, …) — the client serialises
/// with PropertyNamingPolicy = null and omits null fields.
/// </summary>
public class FccmOnboardingPayload
{
    // Lower-cased on purpose: legacy sends "applicationId" / "requestUserId".
    public string? applicationId { get; set; }
    public string? requestUserId { get; set; }
    public FccmOnboardingCustomer OnboardingCustomer { get; set; } = new();
}

/// <summary>
/// The legacy <c>Onboardingcustomer</c> field set. The customer-identity +
/// classification fields are populated from the transaction's customer
/// snapshot; the risk-parameter fields (CUST_PERF / DEF_SECT / HS_CD /
/// StructBus / OVER_IMP) and the multi-value-derived fields are left null
/// until the KYC Section-A capture + the FCCM Oracle risk-param lookup
/// (FCC_OB_RISK_PRMS_CODE_SCRE_MAP) are ported — see the onboarding-payload
/// note in BE_revamp_Changes.md.
/// </summary>
public class FccmOnboardingCustomer
{
    public string? ApplicantID { get; set; }
    public string? CustomerType { get; set; }
    public string? Jurisdiction { get; set; }
    public string? CountryOfResidence { get; set; }
    public string? CustomerName { get; set; }
    public string? DisplayName { get; set; }
    // The upstream expects these name parts present (the verified sample
    // sends FirstName + empty LastName/MiddleName even for an org).
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? MiddleName { get; set; }
    public string? OrganizationName { get; set; }
    public string? BusinessDomain { get; set; }
    public string? Industry { get; set; }
    public string? OrganizationLegalStructure { get; set; }
    public string? CountryOfIncorporation { get; set; }
    public string? RelationshipTeamCode { get; set; }
    public string? TypeofRequest { get; set; }
    // Remaining legacy classification/identity fields (DERIVED from KYC form
    // data by KycOnboardingFieldResolver — full legacy parity). Null-omitted
    // on the wire; gated by FccmOnboardingOptions.IncludeRiskParameterFields
    // because the current FCCM environment's strict schema accepts only a
    // subset (legacy environment accepted the full set).
    public string? EmploymentType { get; set; }
    public string? CountryOfBirth { get; set; }
    public string? WealthSource { get; set; }
    public string? EmployerName { get; set; }
    public string? CitizenshipStatus { get; set; }
    public string? PrimaryCitizenship { get; set; }
    public string? SecondaryCitizenship { get; set; }
    public string? CustomerNAICSCode { get; set; }
    public string? Occupation { get; set; }
    public string? NumberOfDependents { get; set; }
    public string? JobTitle { get; set; }
    public string? Age { get; set; }
    public string? RetirementYear { get; set; }
    public string? EmploymentStatus { get; set; }
    public string? Alias { get; set; }
    // Risk parameters (legacy derives these from the KYC grids via the FCCM
    // Oracle FCC_OB_RISK_PRMS_CODE_SCRE_MAP max-score lookup).
    public string? CUST_PERF { get; set; }
    public string? DEF_SECT { get; set; }
    public string? HS_CD { get; set; }
    public string? StructBus { get; set; }
    public string? OVER_IMP { get; set; }
}
