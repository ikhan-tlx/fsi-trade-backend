-- =========================================================================
-- 2026_05_011_CreateFlagCatalogue.sql
--
-- Slice 8 Step 1 — six-table schema for the new Flag catalogue + the
-- generic document store that backs evidence attachments.
--
-- Why this exists
-- ---------------
-- Today the bank's "manual red flags" are dynamic-form fields stored in
-- TmX_Tenant_Field_Setup (one row per product × subtype × slot) with
-- their values written into TmX_Transaction_Detail.UDF_Data as JSON keys
-- (e.g. "ILFMRL5": "1"). This makes stats impossible without JSON-
-- parsing on every transaction row, and duplicates the indicator text
-- across ~488 rows (post-Dummy filter) + the legacy integration source
-- (TradeController.cs).
--
-- The new model is a clean first-class entity with six tables:
--
--   TmX_Document                    generic file-attachment store
--                                   (used by flag evidence today,
--                                    open to future consumers)
--   TmX_Flag_Catalogue              the WHAT   (master definitions)
--   TmX_Flag_Catalogue_Locale       the WHAT   (per-locale translations)
--   TmX_Flag_Scope                  the WHERE  (which product/tab carries it)
--   TmX_Transaction_Flag            the IS-IT-SET (current state per txn)
--   TmX_Transaction_Flag_History    the WHEN/BY-WHOM (full audit trail)
--
-- Scope strategy: aligns with the existing form structure by keying off
-- the existing TmX_Tab.Tab_ID rather than inventing a parallel Subtype
-- table. The legacy *MRL* field-name prefix (ILF, ICR, SG, ...) is
-- preserved on the scope row's Legacy_Field_Name column for forensic
-- traceability, but isn't used for runtime resolution.
--
-- Document store: deliberately NOT reusing TmX_Application_Checklist —
-- that table is actively used by the legacy app with Azure Blob URLs,
-- carries verification-flow columns (Verification_Required) that don't
-- apply to flag evidence, and is semantically a "checklist row" rather
-- than a generic doc. TmX_Document is storage-provider-agnostic via
-- Storage_Provider_Lkp_ID — LOCAL_DISK today (ICBC_Data folder on the
-- deployed server), Azure / S3 later if infrastructure changes.
--
-- Idempotent: every CREATE is guarded by IF NOT EXISTS. Safe to re-run.
--
-- Run on: ICBC_DEMO
-- =========================================================================

USE [ICBC_DEMO];
GO

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
SET NOCOUNT ON;
GO

-- -------------------------------------------------------------------------
-- 0. TmX_Document — generic file-attachment store.
--    One row per uploaded file. Storage provider is lookup-driven so the
--    same table can sit in front of local-disk uploads (today),
--    Azure Blob, S3, or whatever the bank moves to. The referring table
--    (e.g. TmX_Transaction_Flag.Evidence_Document_ID) holds the FK — we
--    keep linkage one-way to dodge the polymorphic-association mess.
-- -------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables
               WHERE name = 'TmX_Document' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    PRINT 'Creating dbo.TmX_Document';

    CREATE TABLE [dbo].[TmX_Document](
        [Document_ID]              [int] IDENTITY(1,1) NOT NULL,

        -- Filename as the user uploaded it. Used for download UX
        -- (Content-Disposition) and audit; NOT used for on-disk storage.
        [Original_File_Name]       [nvarchar](255) NOT NULL,

        -- GUID-based filename on disk (or blob key for cloud providers).
        -- Format: "<guid>.<ext>" — keeps directories conflict-free even
        -- under concurrent upload.
        [Stored_File_Name]         [nvarchar](255) NOT NULL,

        [Mime_Type]                [nvarchar](100) NULL,
        [File_Size_Bytes]          [bigint] NULL,

        -- SHA-256 of file contents. Enables integrity check on retrieval
        -- and content-based dedupe later if storage costs become a concern.
        [Sha256_Hash]              [char](64) NULL,

        -- FK to TmX_Lookup row with Lookup_Type='STORAGE_PROVIDER'.
        -- Seeds: LOCAL_DISK, AZURE_BLOB, S3.
        [Storage_Provider_Lkp_ID]  [int] NOT NULL,

        -- Relative path within the storage provider. For LOCAL_DISK this
        -- is appended to the base path from appsettings
        -- (Documents:StoragePath, e.g. "ICBC_Data"). Format the consumer
        -- writes: "<module>/<yyyy>/<MM>/<stored-file-name>" so any single
        -- directory stays small.
        [Storage_Relative_Path]    [nvarchar](500) NOT NULL,

        [Tenant_ID]                [int] NOT NULL,
        [Active_Flag]              [bit] NOT NULL CONSTRAINT [DF_Document_ActiveFlag] DEFAULT (1),

        [Uploaded_By]              [nvarchar](100) NOT NULL,
        [Uploaded_Date]            [datetime] NOT NULL,
        [Created_By]               [nvarchar](100) NOT NULL,
        [Created_Date]             [datetime] NOT NULL,
        [Last_Updated_By]          [nvarchar](100) NULL,
        [Last_Updated_Date]        [datetime] NULL,

        CONSTRAINT [XPKTmX_Document] PRIMARY KEY CLUSTERED ([Document_ID] ASC)
    ) ON [PRIMARY];

    -- Active-only document filter is the dominant query pattern.
    CREATE INDEX [IX_TmX_Document_Active_Uploaded]
        ON [dbo].[TmX_Document] ([Active_Flag], [Uploaded_Date] DESC)
        INCLUDE ([Document_ID], [Original_File_Name], [Storage_Provider_Lkp_ID]);

    -- Hash lookup for dedupe-by-content (future optimisation).
    CREATE INDEX [IX_TmX_Document_Sha256]
        ON [dbo].[TmX_Document] ([Sha256_Hash])
        WHERE [Sha256_Hash] IS NOT NULL;
END
GO

-- -------------------------------------------------------------------------
-- 1. TmX_Flag_Catalogue — master flag definitions.
--    One row per distinct indicator text. The "what" of the flag.
-- -------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables
               WHERE name = 'TmX_Flag_Catalogue' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    PRINT 'Creating dbo.TmX_Flag_Catalogue';

    CREATE TABLE [dbo].[TmX_Flag_Catalogue](
        [Flag_ID]              [int] IDENTITY(1,1) NOT NULL,

        -- Stable code derived from the indicator text. Used by integrations
        -- and APIs that need a string handle rather than an integer ID.
        -- Format: "<CATEGORY>.<TYPE>.<short-hash>"  e.g. "TBML.MRL.A1B2C3D4"
        [Flag_Code]            [nvarchar](100) NOT NULL,

        -- Short label for grids / dropdowns. First ~100 chars of the
        -- indicator. Catalogue admins can edit this once we have a UI.
        [Flag_Name]            [nvarchar](200) NOT NULL,

        -- Full indicator text — the analyst-facing description.
        [Flag_Description]     [nvarchar](max) NOT NULL,

        -- FK to TmX_Lookup row with Lookup_Type='FLAG_TYPE'.
        -- Seeds: Manual, Automated.
        [Flag_Type_Lkp_ID]     [int] NOT NULL,

        -- FK to TmX_Lookup row with Lookup_Type='FLAG_CATEGORY'.
        -- Seeds: TBML, KYC, Onboarding, Generic.
        [Flag_Category_Lkp_ID] [int] NULL,

        -- FK to TmX_Lookup row with Lookup_Type='FLAG_SEVERITY'.
        -- Seeds: Critical, High, Medium, Low, Info.
        [Severity_Lkp_ID]      [int] NULL,

        -- Risk-score contribution when this flag is set on a transaction.
        -- Default 1.00 — admin tunes per flag via the catalogue UI.
        [Default_Weight]       [decimal](8, 2) NOT NULL CONSTRAINT [DF_FlagCatalogue_DefaultWeight] DEFAULT (1.00),

        -- Whether an attachment is required when this flag is ticked.
        -- Legacy 'checkbox_file' field-type implies yes for the MRL set.
        [Requires_Evidence]    [bit] NOT NULL CONSTRAINT [DF_FlagCatalogue_RequiresEvidence] DEFAULT (0),

        -- Optional — when the flag originated from an upstream system
        -- (BRAINS, FCCM). NULL means analyst-set / manual catalogue entry.
        [Source_System]        [nvarchar](50) NULL,

        -- Catalogue-level active flag. A flag can be retired by setting
        -- this to 0; existing TmX_Transaction_Flag rows are preserved.
        [Active_Flag]          [bit] NOT NULL CONSTRAINT [DF_FlagCatalogue_ActiveFlag] DEFAULT (1),

        [Created_By]           [nvarchar](100) NOT NULL,
        [Created_Date]         [datetime] NOT NULL,
        [Last_Updated_By]      [nvarchar](100) NULL,
        [Last_Updated_Date]    [datetime] NULL,

        CONSTRAINT [XPKTmX_Flag_Catalogue] PRIMARY KEY CLUSTERED ([Flag_ID] ASC),
        CONSTRAINT [UQ_TmX_Flag_Catalogue_Flag_Code] UNIQUE ([Flag_Code])
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY];

    -- Hot-path index: stats queries filter by category + type a lot.
    CREATE INDEX [IX_TmX_Flag_Catalogue_Category_Type]
        ON [dbo].[TmX_Flag_Catalogue] ([Flag_Category_Lkp_ID], [Flag_Type_Lkp_ID])
        WHERE [Active_Flag] = 1;
END
GO

-- -------------------------------------------------------------------------
-- 2. TmX_Flag_Catalogue_Locale — translations.
--    One row per (Flag_ID, Locale_ID). Catalogue row carries the canonical
--    (typically English) text; this table holds the rest.
-- -------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables
               WHERE name = 'TmX_Flag_Catalogue_Locale' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    PRINT 'Creating dbo.TmX_Flag_Catalogue_Locale';

    CREATE TABLE [dbo].[TmX_Flag_Catalogue_Locale](
        [Flag_Catalogue_Locale_ID] [int] IDENTITY(1,1) NOT NULL,
        [Flag_ID]                  [int] NOT NULL,
        [Locale_ID]                [int] NOT NULL,
        [Locale_Name]              [nvarchar](200) NULL,
        [Locale_Description]       [nvarchar](max) NULL,
        [Created_By]               [nvarchar](100) NOT NULL,
        [Created_Date]             [datetime] NOT NULL,
        [Last_Updated_By]          [nvarchar](100) NULL,
        [Last_Updated_Date]        [datetime] NULL,

        CONSTRAINT [XPKTmX_Flag_Catalogue_Locale]
            PRIMARY KEY CLUSTERED ([Flag_Catalogue_Locale_ID] ASC),

        CONSTRAINT [UQ_TmX_Flag_Catalogue_Locale_FlagLocale]
            UNIQUE ([Flag_ID], [Locale_ID]),

        CONSTRAINT [FK_TmX_Flag_Catalogue_Locale_Flag]
            FOREIGN KEY ([Flag_ID]) REFERENCES [dbo].[TmX_Flag_Catalogue] ([Flag_ID])
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY];
END
GO

-- -------------------------------------------------------------------------
-- 3. TmX_Flag_Scope — which (Product, Tab) carries which flag.
--    One row per source TmX_Tenant_Field_Setup MRL row. The same Flag_ID
--    can appear under multiple (Product_ID, Tab_ID) combinations — that's
--    how the migration de-duplicates the indicator text while preserving
--    every form-rendering location.
-- -------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables
               WHERE name = 'TmX_Flag_Scope' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    PRINT 'Creating dbo.TmX_Flag_Scope';

    CREATE TABLE [dbo].[TmX_Flag_Scope](
        [Flag_Scope_ID]       [int] IDENTITY(1,1) NOT NULL,
        [Flag_ID]             [int] NOT NULL,
        [Product_ID]          [int] NOT NULL,

        -- NULL = product-level (no further tab discriminator). Today only
        -- reserved for future use; the migration populates Tab_ID for every
        -- legacy row (including KYC, which lives on Tab 34).
        [Tab_ID]              [int] NULL,

        [Sort_Order]          [int] NOT NULL CONSTRAINT [DF_FlagScope_SortOrder] DEFAULT (0),

        -- Per-scope active flag. Source rows with Visibility='0' migrate
        -- as Active_Flag=0 — the catalogue row itself stays alive because
        -- the indicator might be active on a DIFFERENT product.
        [Active_Flag]         [bit] NOT NULL CONSTRAINT [DF_FlagScope_ActiveFlag] DEFAULT (1),

        -- Original TmX_Tenant_Field_Setup.Field_Name (e.g. "ILFMRL5").
        -- Used by the UDF_Data backfill to find the JSON key for this scope.
        -- Also the bridge for any FE compatibility layer during the
        -- dual-write transition (Phase 3 of the migration plan).
        [Legacy_Field_Name]   [nvarchar](50) NULL,

        [Created_By]          [nvarchar](100) NOT NULL,
        [Created_Date]        [datetime] NOT NULL,
        [Last_Updated_By]     [nvarchar](100) NULL,
        [Last_Updated_Date]   [datetime] NULL,

        CONSTRAINT [XPKTmX_Flag_Scope] PRIMARY KEY CLUSTERED ([Flag_Scope_ID] ASC),

        CONSTRAINT [FK_TmX_Flag_Scope_Flag]
            FOREIGN KEY ([Flag_ID]) REFERENCES [dbo].[TmX_Flag_Catalogue] ([Flag_ID])
    ) ON [PRIMARY];

    -- Filtered unique indexes — separate handling for NULL Tab_ID per
    -- SQL Server's NULL=NULL behaviour in unique constraints. Result:
    --   • A flag can have at most one (Product_ID, Tab_ID) row per Tab_ID
    --   • A flag can have at most one (Product_ID, NULL) "product-level" row
    CREATE UNIQUE INDEX [UQ_TmX_Flag_Scope_FlagProductTab]
        ON [dbo].[TmX_Flag_Scope] ([Flag_ID], [Product_ID], [Tab_ID])
        WHERE [Tab_ID] IS NOT NULL;

    CREATE UNIQUE INDEX [UQ_TmX_Flag_Scope_FlagProduct_NoTab]
        ON [dbo].[TmX_Flag_Scope] ([Flag_ID], [Product_ID])
        WHERE [Tab_ID] IS NULL;

    -- "Show me every flag for this (Product, Tab)" — the form-render path.
    CREATE INDEX [IX_TmX_Flag_Scope_ProductTab]
        ON [dbo].[TmX_Flag_Scope] ([Product_ID], [Tab_ID])
        INCLUDE ([Flag_ID], [Sort_Order])
        WHERE [Active_Flag] = 1;

    -- "Resolve the Flag_ID for this legacy field name" — backfill path.
    CREATE INDEX [IX_TmX_Flag_Scope_LegacyFieldName]
        ON [dbo].[TmX_Flag_Scope] ([Legacy_Field_Name]);
END
GO

-- -------------------------------------------------------------------------
-- 4. TmX_Transaction_Flag — current state per transaction.
--    One row per (Transaction_ID, Flag_ID). Last write wins; the history
--    table captures every prior state.
-- -------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables
               WHERE name = 'TmX_Transaction_Flag' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    PRINT 'Creating dbo.TmX_Transaction_Flag';

    CREATE TABLE [dbo].[TmX_Transaction_Flag](
        [Transaction_Flag_ID]   [int] IDENTITY(1,1) NOT NULL,
        [Transaction_ID]        [int] NOT NULL,
        [Flag_ID]               [int] NOT NULL,
        [Is_Flagged]            [bit] NOT NULL,

        -- FK to TmX_Document — the generic file-attachment store.
        -- Nullable because not every flag has supporting evidence; the
        -- catalogue's Requires_Evidence column drives the app-layer
        -- "must attach a document" validation, not a NOT NULL constraint.
        [Evidence_Document_ID]  [int] NULL,

        -- Analyst comment captured at the time of flagging. Useful for
        -- the audit trail without needing to dig through history.
        [Analyst_Notes]         [nvarchar](max) NULL,

        -- Attribution for the last set/clear.
        [Set_By]                [nvarchar](100) NOT NULL,
        [Set_Date]              [datetime] NOT NULL,

        [Created_By]            [nvarchar](100) NOT NULL,
        [Created_Date]          [datetime] NOT NULL,
        [Last_Updated_By]       [nvarchar](100) NULL,
        [Last_Updated_Date]     [datetime] NULL,

        CONSTRAINT [XPKTmX_Transaction_Flag] PRIMARY KEY CLUSTERED ([Transaction_Flag_ID] ASC),
        CONSTRAINT [UQ_TmX_Transaction_Flag_TxnFlag]
            UNIQUE ([Transaction_ID], [Flag_ID]),

        CONSTRAINT [FK_TmX_Transaction_Flag_Flag]
            FOREIGN KEY ([Flag_ID]) REFERENCES [dbo].[TmX_Flag_Catalogue] ([Flag_ID]),

        CONSTRAINT [FK_TmX_Transaction_Flag_Document]
            FOREIGN KEY ([Evidence_Document_ID]) REFERENCES [dbo].[TmX_Document] ([Document_ID])
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY];

    -- "Show every flag for this transaction" — the detail-page query.
    CREATE INDEX [IX_TmX_Transaction_Flag_Transaction]
        ON [dbo].[TmX_Transaction_Flag] ([Transaction_ID])
        INCLUDE ([Flag_ID], [Is_Flagged]);

    -- "How many transactions are currently flagged with X" — stats query.
    CREATE INDEX [IX_TmX_Transaction_Flag_Flag_IsFlagged]
        ON [dbo].[TmX_Transaction_Flag] ([Flag_ID], [Is_Flagged]);
END
GO

-- -------------------------------------------------------------------------
-- 5. TmX_Transaction_Flag_History — append-only audit trail.
--    One row per state change. Denormalises Transaction_ID and Flag_ID
--    onto the history table so audit queries don't need to JOIN back to
--    TmX_Transaction_Flag (which may have been updated since).
-- -------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables
               WHERE name = 'TmX_Transaction_Flag_History' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    PRINT 'Creating dbo.TmX_Transaction_Flag_History';

    CREATE TABLE [dbo].[TmX_Transaction_Flag_History](
        [Transaction_Flag_History_ID]  [bigint] IDENTITY(1,1) NOT NULL,
        [Transaction_Flag_ID]          [int] NOT NULL,

        -- Denormalised for fast filtering on (Transaction_ID, Changed_Date)
        -- and (Flag_ID, Changed_Date) without JOIN-back.
        [Transaction_ID]               [int] NOT NULL,
        [Flag_ID]                      [int] NOT NULL,

        -- FK to TmX_Lookup row with Lookup_Type='FLAG_CHANGE_TYPE'.
        -- Seeds: Set, Cleared, Evidence_Attached, Evidence_Removed, Notes_Updated.
        [Change_Type_Lkp_ID]           [int] NOT NULL,

        [Previous_Is_Flagged]          [bit] NULL,
        [New_Is_Flagged]               [bit] NULL,
        [Previous_Notes]               [nvarchar](max) NULL,
        [New_Notes]                    [nvarchar](max) NULL,
        [Previous_Evidence_Document_ID][int] NULL,
        [New_Evidence_Document_ID]     [int] NULL,

        [Changed_By]                   [nvarchar](100) NOT NULL,
        [Changed_Date]                 [datetime] NOT NULL,

        CONSTRAINT [XPKTmX_Transaction_Flag_History]
            PRIMARY KEY CLUSTERED ([Transaction_Flag_History_ID] ASC),

        CONSTRAINT [FK_TmX_Transaction_Flag_History_TransactionFlag]
            FOREIGN KEY ([Transaction_Flag_ID])
            REFERENCES [dbo].[TmX_Transaction_Flag] ([Transaction_Flag_ID])
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY];

    -- Chronological per-transaction view (audit panel on the detail page).
    CREATE INDEX [IX_TmX_Transaction_Flag_History_Transaction_Date]
        ON [dbo].[TmX_Transaction_Flag_History] ([Transaction_ID], [Changed_Date] DESC);

    -- Stats over time — "flag-toggle rate by month".
    CREATE INDEX [IX_TmX_Transaction_Flag_History_Flag_Date]
        ON [dbo].[TmX_Transaction_Flag_History] ([Flag_ID], [Changed_Date] DESC);

    -- By-user audit — "what did this analyst flag this week".
    CREATE INDEX [IX_TmX_Transaction_Flag_History_User_Date]
        ON [dbo].[TmX_Transaction_Flag_History] ([Changed_By], [Changed_Date] DESC);
END
GO

PRINT 'Slice 8 Step 1 — Flag catalogue schema created (6 tables, 13 indexes).';
