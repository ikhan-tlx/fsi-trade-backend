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

    // ── Static classification defaults (legacy hardcoded-builder fallback ONLY) ──
    // These feed the built-in hardcoded payload builder that runs only if no mapping
    // profile is active. Since MappingProfile defaults to "v1", the mapping engine is
    // always used and these are NOT consulted — the real constants live in the profile
    // file (onboarding.v1/v2.mapping.json). They are kept (with code defaults) purely
    // as an internal safety net and are intentionally NOT exposed in appsettings.
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

    // ── Configurable payload mapping (externalized field mappings) ──
    /// <summary>
    /// Onboarding payload profile to build. The engine loads
    /// <c>onboarding.{MappingProfile}.mapping.json</c> from <see cref="MappingDirectory"/>.
    /// Shipped profiles: <c>"v1"</c> (flat legacy KYCRequestModel shape) and
    /// <c>"v2"</c> (rich nested OB v2 shape); any custom profile name works if the file
    /// exists. This is the single switch for the payload shape — a config change, not code.
    /// <para><b>Default is <c>"v2"</c>.</b> The active value normally comes from the
    /// Configuration module (Integration.Onboarding.MappingProfile, Default_Value 'v2'),
    /// bridged into IConfiguration. This code default is the last-resort fallback for a
    /// DB-down boot (the bridge is fail-soft), and must match the DB default so behaviour
    /// is identical whether or not the config table is reachable (see
    /// <see cref="EffectiveMappingProfile"/>).</para>
    /// </summary>
    public string MappingProfile { get; set; } = "v2";

    /// <summary>The profile the engine actually uses: <see cref="MappingProfile"/> when
    /// set, otherwise the default <c>"v2"</c>. Guarantees a valid profile is always chosen.</summary>
    public string EffectiveMappingProfile
        => string.IsNullOrWhiteSpace(MappingProfile) ? "v2" : MappingProfile.Trim();

    /// <summary>
    /// Directory holding <c>onboarding.*.mapping.json</c> profiles. Relative to the
    /// app content root, or an absolute path. Default <c>"Mappings/onboarding"</c>.
    /// </summary>
    public string MappingDirectory { get; set; } = "Mappings/onboarding";

    /// <summary>
    /// When true, mapping profiles are re-read from disk on every submission
    /// (handy for tuning a client's mapping without a redeploy). Leave false in
    /// production so profiles are cached + immutable per process.
    /// </summary>
    public bool MappingHotReload { get; set; } = false;
}
