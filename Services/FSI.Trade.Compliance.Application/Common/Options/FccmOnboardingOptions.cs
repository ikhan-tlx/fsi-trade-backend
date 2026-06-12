namespace FSI.Trade.Compliance.Application.Common.Options;

/// <summary>
/// Environment-configurable settings for the KYC→FCCM onboarding submission
/// (<c>InitiateOnboardingService/OB/Initiate</c>). Bound from the
/// <c>Integration:Onboarding</c> section. Everything that differs between FCCM
/// integration environments — auth, the static classification defaults, and
/// which optional fields the target schema accepts — lives here so switching
/// environments is a config change, NOT a code edit.
///
/// Rationale: the legacy FCCM schema REQUIRED <c>CustomerName</c>; the current
/// target environment REJECTS it as an extra property (HTTP 400). Rather than
/// hard-code one behaviour, <see cref="IncludeCustomerName"/> toggles it. Same
/// idea for the default classification values and the applicationId pattern.
/// </summary>
public class FccmOnboardingOptions
{
    public const string SectionName = "Integration:Onboarding";

    /// <summary>HTTP Basic-auth username (legacy: <c>KYCUsername</c>). Empty = no auth header.</summary>
    public string Username { get; set; } = "";

    /// <summary>HTTP Basic-auth password (legacy: <c>KYCPassword</c>).</summary>
    public string Password { get; set; } = "";

    /// <summary>
    /// <c>applicationId</c> format string. <c>{0}</c> is replaced with the
    /// resolved applicant id (customer code). Default <c>"CIF-{0}"</c> matches
    /// the legacy convention. Set to <c>"{0}"</c> to send the bare code.
    /// </summary>
    public string ApplicationIdFormat { get; set; } = "CIF-{0}";

    // ── Static classification defaults applied to every OnboardingCustomer ──
    // Used until the KYC Section-A capture supplies real per-customer values.
    public string CustomerType       { get; set; } = "ORG";
    public string Jurisdiction       { get; set; } = "PAK";
    public string CountryOfResidence { get; set; } = "PAK";
    public string BusinessDomain     { get; set; } = "Trade";

    // ── Field-inclusion toggles (target-schema differences) ──
    /// <summary>
    /// Whether to send <c>CustomerName</c> in <c>OnboardingCustomer</c>. The
    /// current FCCM environment rejects it (strict schema → HTTP 400); the
    /// legacy environment required it. Default <c>false</c> (current env).
    /// When false, <c>DisplayName</c> + <c>OrganizationName</c> carry the name.
    /// </summary>
    public bool IncludeCustomerName { get; set; } = false;

    /// <summary>
    /// Whether to send the name-part fields (<c>FirstName</c> / <c>LastName</c>
    /// / <c>MiddleName</c>). The verified sample sends them (FirstName=name,
    /// empty Last/Middle) even for an org. Default <c>true</c>.
    /// </summary>
    public bool IncludeNameParts { get; set; } = true;

    /// <summary>
    /// Whether to send the FULL legacy risk-parameter / classification field set
    /// (EmploymentType, CountryOfBirth, WealthSource, Industry, CUST_PERF,
    /// DEF_SECT, HS_CD, StructBus, OVER_IMP, NAICS, etc.) derived by
    /// <c>KycOnboardingFieldResolver</c> from the KYC grids via the Oracle
    /// FCC_OB_RISK_PRMS_CODE_SCRE_MAP max-score lookup.
    ///
    /// The legacy FCCM environment ACCEPTED this whole set; the current target
    /// environment does strict schema validation and may reject unknown
    /// properties (it already rejected CustomerName — HTTP 400). Default
    /// <c>false</c> so the proven-working minimal submit is not regressed. Set
    /// <c>true</c> only against an environment whose schema accepts the full
    /// set. The derivation runs regardless; this only governs what is emitted.
    /// The always-safe derived fields (CountryOfResidence, Industry,
    /// CountryOfIncorporation, RelationshipTeamCode, TypeofRequest,
    /// OrganizationLegalStructure) are sent even when this is false, with the
    /// configured static value as a fallback when the derivation yields null.
    /// </summary>
    public bool IncludeRiskParameterFields { get; set; } = false;
}
