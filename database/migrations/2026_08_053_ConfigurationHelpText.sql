-- =========================================================================
-- 2026_08_053_ConfigurationHelpText.sql
-- -------------------------------------------------------------------------
-- Adds a Help_Detail column to TmX_Configuration_Definition and seeds, for
-- every setting, a short one-line Description (shown under the name on the
-- Configuration screen) plus a Help_Detail (the "what this actually does"
-- text shown in the (i) info popover). Detail text is grounded in the real
-- consumer (service / stored-proc param / options class), not guessed.
--
-- Idempotent: column guarded by COL_LENGTH; text applied via UPDATE from a
-- staging table keyed by Configuration_Key. Safe to re-run / edit values.
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -b -i "<path>\2026_08_053_ConfigurationHelpText.sql"
-- =========================================================================

USE [ICBC_DEMO];
GO
SET NOCOUNT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('dbo.TmX_Configuration_Definition','Help_Detail') IS NULL
BEGIN
    ALTER TABLE dbo.TmX_Configuration_Definition ADD Help_Detail nvarchar(max) NULL;
    PRINT 'Added column Help_Detail.';
END
GO

DECLARE @H TABLE (K nvarchar(200) PRIMARY KEY, Summary nvarchar(1000), Detail nvarchar(max));
INSERT INTO @H (K, Summary, Detail) VALUES
-- ===== Security / Session =====
(N'Jwt.AccessTokenDefaultMinutes', N'Default sign-in (access-token) lifetime, in minutes.',
 N'Default JWT access-token lifetime issued at login by JwtTokenService.ResolveTtl. If the user holds a role with a shorter per-role TTL (Jwt.RoleTtlMinutes), the smallest wins. When it elapses the session expires and the re-auth modal appears. Applies to tokens issued after the change.'),
(N'Jwt.RefreshTokenFactor', N'Refresh-token lifetime as a multiple of the access-token lifetime.',
 N'The refresh token TTL = access-token minutes x this factor (RefreshTokenStore). A larger factor lets a session be silently renewed for longer after the access token expires.'),
(N'Jwt.RoleTtlMinutes', N'Per-role overrides for token lifetime (JSON: role to minutes).',
 N'Map of role name to access-token minutes. JwtTokenService.ResolveTtl picks the SMALLEST matching role TTL, else falls back to Jwt.AccessTokenDefaultMinutes. Lets high-privilege roles get shorter sessions.'),
(N'Auth.RestrictConcurrentLogin', N'Allow only one active session per user.',
 N'When on, a new login invalidates the user other active session (single concurrent session), enforced in the auth pipeline.'),
(N'Auth.RequireDeviceIdHeader', N'Require the per-device X-Device-Id header on API calls.',
 N'When on, DeviceTrackingMiddleware requires the device-id header and binds the JWT to that device; a missing/mismatched device is rejected (device_unknown). Header name = Auth.DeviceIdHeaderName.'),
(N'Auth.DeviceIdHeaderName', N'Name of the per-device id HTTP header.',
 N'The header the device-tracking middleware reads and binds the session to (default X-Device-Id). Structural - applies on restart.'),
(N'TwoFactor.Enabled', N'Turn two-factor authentication on or off.',
 N'Master switch for 2FA at login (TwoFactorOptions.Enabled). When off, login is single-factor.'),
(N'TwoFactor.Method', N'Second-factor method (Email / SMS / Totp / None).',
 N'Which 2FA channel is used when TwoFactor.Enabled is on (TwoFactorOptions.Method).'),
-- ===== Password Policy =====
(N'PasswordPolicy.MaxFailedAttempts', N'Failed logins allowed before the account locks.',
 N'After this many consecutive failed logins the account is locked for PasswordPolicy.LockoutMinutes (read as PasswordPolicy:MaxFailedAttempts in the auth flow).'),
(N'PasswordPolicy.LockoutMinutes', N'How long an account stays locked after too many failures.',
 N'Lockout duration in minutes applied once MaxFailedAttempts is reached.'),
(N'PasswordPolicy.EnforceExpiry', N'Require passwords to be changed periodically.',
 N'When on, passwords older than PasswordPolicy.ExpiryDays must be reset; PasswordExpiryScanService warns and forces the change. Roles in DisableExpiryForRoles are exempt.'),
(N'PasswordPolicy.ExpiryDays', N'Days before a password must be changed.',
 N'Password maximum age in days when EnforceExpiry is on.'),
(N'PasswordPolicy.DormancyDays', N'Days of inactivity before an account is dormant.',
 N'When EnforceDormancy is on, an account with no login for this many days is locked as dormant.'),
(N'PasswordPolicy.EnforceFirstPasswordChange', N'Force a password change on first login.',
 N'When on, a newly created user must set a new password before using the app.'),
(N'PasswordPolicy.EnforceDormancy', N'Lock accounts inactive for too long.',
 N'Master switch for the dormancy lockout using PasswordPolicy.DormancyDays.'),
(N'PasswordPolicy.DisableExpiryForRoles', N'Roles exempt from password expiry (JSON array).',
 N'Users whose role is in this list are not subject to EnforceExpiry (e.g. service or customer roles).'),
-- ===== Periodic Review =====
(N'PeriodicReview.Enabled', N'Turn the periodic customer re-review scheduler on or off.',
 N'Master switch for the background PeriodicReviewService that re-reviews onboarded customers on a cadence (PeriodicReviewOptions.Enabled).'),
(N'PeriodicReview.ScanInterval', N'How often the periodic-review scheduler wakes up.',
 N'TimeSpan between scans of the PeriodicReviewService hosted service.'),
(N'PeriodicReview.BatchSize', N'Customers processed per periodic-review scan.',
 N'Maximum rows handled in one scan pass - trades throughput against load.'),
(N'PeriodicReview.LeadTimeDays', N'Days ahead of the due date to start a review.',
 N'A review is initiated this many days before it is actually due.'),
(N'PeriodicReview.DefaultFrequency', N'Default re-review cadence when a band has none (JSON).',
 N'Fallback {Value,Unit} cadence used when a customer risk band is not present in PeriodicReview.Frequencies.'),
(N'PeriodicReview.Frequencies', N'Re-review cadence per risk band (JSON).',
 N'Map of risk band (PEP/High/Medium/Low) to {Value,Unit}; drives when each band is re-reviewed.'),
-- ===== Customer Insight =====
(N'CustomerInsightSnapshot.Enabled', N'Turn the customer-insight snapshot service on or off.',
 N'Master switch for the background snapshot writer that refreshes existing-customer screening data (CustomerInsightSnapshotOptions.Enabled).'),
(N'CustomerInsightSnapshot.CheckInterval', N'How often the customer-insight snapshot runs.',
 N'TimeSpan between snapshot passes.'),
-- ===== Trade Repository =====
(N'TradeRepository.SlaDays', N'Default review SLA for a transaction, in days.',
 N'Review SLA in days used by the Trade Repository (TradeRepositoryOptions.SlaDays).'),
-- ===== SLA / Deadlines =====
(N'Sla.Enabled', N'Turn the SLA-breach notification scanner on or off.',
 N'Master switch for the background SlaBreachScanService.'),
(N'Sla.DefaultHours', N'Deadline window in hours before a work item breaches SLA.',
 N'Hours from when an item enters its current state until it is breached (SlaBreachScanService).'),
(N'Sla.AtRiskPercent', N'Percent of the SLA window at which an at-risk alert fires.',
 N'When elapsed time reaches this percent of Sla.DefaultHours, an at-risk notification is raised.'),
-- ===== FCCM / Integration =====
(N'Integration.FccmOracleConnection', N'Oracle FCCM database connection string (secret).',
 N'ODP.NET connection string FccmOracleReader uses to reach Oracle FCCM/Mantas. Stored ENCRYPTED; set/rotate here; validated by Test connection (SELECT 1 FROM DUAL). Save before testing.'),
(N'Integration.TradeCaseDefaults.Jurisdiction', N'Jurisdiction code stamped on every FCCM case created from a transaction.',
 N'Passed as P_JRSDCN_CD to the Oracle proc PR_CREATE_EVENT_DEMO_CFG (and JRSDCN_CD on the KDD_CASE_CUSTOMERS insert) in FccmOracleReader when a transaction is handed off to FCCM/Mantas. Legacy/default PAK.'),
(N'Integration.TradeCaseDefaults.CaseType', N'FCCM case type for created cases.',
 N'Bound to P_CASE_TYPE of PR_CREATE_EVENT_DEMO_CFG.'),
(N'Integration.TradeCaseDefaults.OwnerOrg', N'Owning organisation code for created FCCM cases.',
 N'Bound to P_OWNER_ORG of PR_CREATE_EVENT_DEMO_CFG.'),
(N'Integration.TradeCaseDefaults.OwnerId', N'Owning user/org id for created FCCM cases.',
 N'Bound to P_OWNER_ID of PR_CREATE_EVENT_DEMO_CFG.'),
(N'Integration.TradeCaseDefaults.BusinessDomain', N'Business-domain code on created FCCM cases.',
 N'Bound to P_BUS_DMN of PR_CREATE_EVENT_DEMO_CFG.'),
(N'Integration.TradeCaseDefaults.EventType', N'Event type on the created FCCM event.',
 N'Bound to P_EVENT_TYPE of PR_CREATE_EVENT_DEMO_CFG.'),
(N'Integration.TradeCaseDefaults.ScenarioClass', N'Scenario class on the created FCCM event.',
 N'Bound to P_SCENARIO_CLASS of PR_CREATE_EVENT_DEMO_CFG.'),
(N'Integration.TradeCaseDefaults.DataOrigin', N'Data-origin code on the created FCCM event.',
 N'Bound to P_DATA_ORIGIN of PR_CREATE_EVENT_DEMO_CFG.'),
(N'Integration.TradeCaseDefaults.EventStatus', N'Initial status of the created FCCM event.',
 N'Bound to P_EVENT_STATUS of PR_CREATE_EVENT_DEMO_CFG.'),
(N'Integration.TradeCaseDefaults.EventPriority', N'Priority of the created FCCM event.',
 N'Bound to P_EVENT_PRIORITY of PR_CREATE_EVENT_DEMO_CFG.'),
(N'Integration.TradeCaseDefaults.CasePriority', N'Priority of the created FCCM case.',
 N'Bound to P_CASE_PRIORITY of PR_CREATE_EVENT_DEMO_CFG.'),
(N'Integration.TradeCaseDefaults.CaseSource', N'Source code of the created FCCM case.',
 N'Bound to P_CASE_SRC of PR_CREATE_EVENT_DEMO_CFG.'),
(N'Integration.TradeCaseDefaults.CaseStatus', N'Initial status of the created FCCM case.',
 N'Bound to P_CASE_STATUS of PR_CREATE_EVENT_DEMO_CFG.'),
(N'Integration.TradeCaseDefaults.ContractEventType', N'Contract event type on the created FCCM event.',
 N'Bound to P_CNTRCT_EVENT_TYPE of PR_CREATE_EVENT_DEMO_CFG.'),
(N'Integration.TradeCaseDefaults.CustomerType', N'Customer type on the FCCM case customer.',
 N'Bound to CUST_TYPE_CD on the KDD_CASE_CUSTOMERS insert in FccmOracleReader.'),
(N'Integration.TradeCaseDefaults.CustomerBusinessDomainList', N'Business-domain list on the FCCM case customer.',
 N'Bound to BUS_DMN_LIST_TX on the KDD_CASE_CUSTOMERS insert in FccmOracleReader.'),
(N'Integration.PollingInterval', N'How often the FCCM case-id poller checks for results.',
 N'TimeSpan between polls of the FccmCaseIdPoller background service reading FCC_OB_RA.'),
(N'Integration.CaseIdTimeout', N'How long to wait for FCCM to return a case id.',
 N'Give-up timeout for the case-id poll before the handoff is treated as unresolved.'),
(N'Integration.HttpTimeout', N'HTTP timeout for FCCM / integration calls.',
 N'Per-call timeout on the integration HttpClient.'),
(N'Integration.FccmOnboardingUrl', N'FCCM onboarding (InitiateOnboarding) endpoint URL.',
 N'The OFSAA InitiateOnboardingService URL the KYC to FCCM onboarding submission posts to. Non-secret; check it with Test URL.'),
(N'Integration.FccmActionCodeStatusMap', N'Maps FCCM callback action codes to transaction statuses (JSON).',
 N'On an FCCM callback the action code is mapped through this table to a Trade transaction status (FccmKycCaseService).'),
(N'Integration.CustomerMasterBaseUrl', N'Base URL of the customer-master service.',
 N'Operational endpoint for customer-master lookups (blank = unset).'),
(N'Integration.FccmOracleSchema', N'Oracle schema for FCCM objects (default MANTAS).',
 N'Sets the session CURRENT_SCHEMA in FccmOracleReader so unqualified FCCM objects resolve; blank falls back to MANTAS.'),
(N'Integration.Onboarding.Username', N'Username for FCCM onboarding HTTP Basic auth.',
 N'Basic-auth user for the InitiateOnboarding call; its password is the encrypted secret Integration.Onboarding.Password.'),
-- ===== Onboarding / External Verification =====
(N'Onboarding.RequireExternalVerification', N'Require external KYC verification at transaction creation.',
 N'Feature flag; when on, creating a KYC transaction requires a successful external provider check before it is allowed.'),
(N'Onboarding.BaseUrl', N'External KYC verification provider URL.',
 N'Endpoint of the third-party screening provider called at KYC creation; check it with Test URL / Test verification call.'),
(N'Onboarding.ResponseMapping', N'Maps the provider raw response to our risk bands (JSON).',
 N'Profile translating provider fields (e.g. STATUS / TRANSOK) into the standard KycResult risk band and pass/adverse decision.'),
(N'Onboarding.FailureHandling', N'What to do when verification is adverse or unreachable.',
 N'Block refuses creation; Allow with flag / Allow with warning proceed with a raised flag or a soft warning.'),
(N'Onboarding.CallTimeout', N'Timeout for the external verification call.',
 N'Per-call timeout for the provider request.'),
(N'Onboarding.ProviderTitle', N'Display name of the external verification provider.',
 N'Vendor-neutral label shown in the UI for the provider.'),
(N'Onboarding.ProviderProfile', N'Provider adapter profile selector.',
 N'Selects the provider profile DynamicKycScreeningService binds (Onboarding:ProviderProfile).'),
-- ===== Reports / Documents =====
(N'Documents.BasePath', N'Root folder for generated/stored documents.',
 N'Base path documents are written under (DocumentOptions.BasePath). System-managed.'),
(N'Reports.ChromiumExecutablePath', N'Path to the Chromium/Edge binary that renders report PDFs.',
 N'Headless browser executable ReportOptions uses to render report PDFs.'),
(N'Reports.ChromiumCachePath', N'Cache / user-data folder for the report renderer.',
 N'Chromium cache directory for the PDF renderer (blank = engine default).'),
(N'Reports.FooterDisclaimer', N'Disclaimer text for report footers (inactive).',
 N'Retained for reference; deactivated because no report currently consumes it.'),
-- ===== Email / SMTP =====
(N'Email.Enabled', N'Turn the notification email channel on or off.',
 N'Master switch (EmailOptions.Enabled). When off, email sending is a no-op.'),
(N'Email.DeliveryMode', N'Email transport: PickupDirectory or Smtp.',
 N'PickupDirectory writes .eml files to a folder (no network); Smtp sends over SMTP.'),
(N'Email.PickupDirectory', N'Folder for generated .eml files in PickupDirectory mode.',
 N'Created if missing; each outbound email is written here as a .eml file.'),
(N'Email.Host', N'SMTP server hostname (Smtp mode).',
 N'Used only when DeliveryMode = Smtp.'),
(N'Email.Port', N'SMTP server port (Smtp mode).',
 N'Used only when DeliveryMode = Smtp.'),
(N'Email.Username', N'SMTP auth username (Smtp mode).',
 N'Used only when DeliveryMode = Smtp; the SMTP password is the encrypted secret Email.Password.'),
(N'Email.UseSsl', N'Use SSL/TLS for the SMTP connection (Smtp mode).',
 N'Enables TLS when connecting to the SMTP server.'),
(N'Email.From', N'Sender address on outbound notification emails.',
 N'The From header used by the notification email sender.'),
(N'Email.MinSeverity', N'Only notifications at or above this severity are emailed.',
 N'Filters which notifications generate an email (info / warning / critical).'),
-- ===== Branding / Appearance =====
(N'Branding.BrandColor', N'Primary brand / logo color (shell and login page).',
 N'Drives the brand accent across the app shell and the pre-auth login page, served by GET /api/v1/Appearance.'),
(N'Branding.AppTitle', N'Application title (browser tab, header, login).',
 N'Shown in the tab, the header and on the login page.'),
(N'Branding.AppSubtitle', N'Subtitle under the title on the login page.',
 N'Shown beneath the app title on the login screen.'),
(N'Branding.DefaultTheme', N'Default UI theme for new sessions.',
 N'Applied for a new session before a user picks their own theme (light / dark / corporate / high-contrast).'),
-- ===== Workflow =====
(N'Workflow.InboxMaxPageSize', N'Maximum page size for the workflow inbox.',
 N'Upper bound on inbox page size (WorkflowOptions.InboxMaxPageSize).'),
-- ===== Diagnostics / Logging =====
(N'Logging.Modules.Api', N'Log level and on/off for the Api log file (JSON).',
 N'{enabled,level} for the Api Serilog file (request/response + lifecycle). Phase 2 wires the live level switch.'),
(N'Logging.Modules.Application.Trade', N'Log level and on/off for the Application.Trade log file (JSON).',
 N'{enabled,level} for the Trade module file (Transaction/KYC/Flags/Workflow + FCCM onboarding detail). Debug adds full payloads.'),
(N'Logging.Modules.Application.UserManagement', N'Log level and on/off for the UserManagement log file (JSON).',
 N'{enabled,level} for the Auth/Users/Roles log file.'),
(N'Logging.Modules.BackgroundService', N'Log level and on/off for the BackgroundService log file (JSON).',
 N'{enabled,level} for hosted services (FccmCaseIdPoller, LogArchiver, scanners).'),
(N'Logging.Directory', N'Folder where log files are written.',
 N'Serilog sink directory. Changing it applies on restart.'),
(N'Logging.RetentionDays', N'Days to keep rolled log files before archiving.',
 N'The LogArchiverService zips files older than this into a backlog archive.'),
(N'Logging.ArchiveScanIntervalHours', N'How often the log archiver runs.',
 N'Interval at which LogArchiverService scans for rolled files to zip.'),
-- ===== Branding (secondary) + secrets =====
(N'Branding.BrandGold', N'Secondary (gold) brand color for accents.',
 N'Secondary brand accent used alongside Branding.BrandColor in the shell and login page.'),
(N'Email.Password', N'SMTP auth password (secret).',
 N'Encrypted SMTP password used in Smtp delivery mode; set here, stored ENCRYPTED, never displayed or logged.'),
(N'Integration.Onboarding.Password', N'Password for FCCM onboarding HTTP Basic auth (secret).',
 N'Encrypted Basic-auth password for the InitiateOnboarding call; paired with Integration.Onboarding.Username. Stored ENCRYPTED, never displayed.');

-- unknown keys (typo / not in this catalogue) — informational only
IF EXISTS (SELECT 1 FROM @H h WHERE NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Definition d WHERE d.Configuration_Key = h.K))
BEGIN
    PRINT '(i) Help rows that did not match a definition (ignored):';
    SELECT h.K AS Unmatched_Key FROM @H h
    WHERE NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Definition d WHERE d.Configuration_Key = h.K);
END;

-- apply summary (Description) + detail (Help_Detail)
UPDATE d
   SET d.Description = h.Summary,
       d.Help_Detail = h.Detail,
       d.Last_Updated_By = N'System (2026_08_053)',
       d.Last_Updated_Date = SYSUTCDATETIME()
FROM dbo.TmX_Configuration_Definition d
JOIN @H h ON h.K = d.Configuration_Key;
PRINT CONCAT('Help text applied to definitions: ', @@ROWCOUNT);

-- definitions still missing help text (so gaps are visible)
PRINT '(i) Active definitions with NO help detail yet:';
SELECT d.Category_Key AS Category, d.Configuration_Key AS [Key]
FROM   dbo.TmX_Configuration_Definition d
WHERE  d.Is_Active = 1 AND (d.Help_Detail IS NULL OR LTRIM(RTRIM(d.Help_Detail)) = N'')
ORDER BY d.Category_Key, d.Configuration_Key;
GO

PRINT '2026_08_053 complete - configuration help text (summary + detail) seeded.';
GO
