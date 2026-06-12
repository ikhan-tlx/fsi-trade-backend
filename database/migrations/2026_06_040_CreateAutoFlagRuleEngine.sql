/* ====================================================================
   2026_06_040_CreateAutoFlagRuleEngine.sql

   Auto Flag rule engine — configuration tables for the new
   configuration-driven automatic red flags (companion to the
   /admin/auto-flags screen). Phase 2 (b) foundation.

   Adds (all NEW / additive — no drops, fully idempotent / re-runnable):
     - TmX_Flag_Rule            : declarative rule rows per flag
                                  (left operand · operator · right operand,
                                   AND/OR groups, evaluation order)
     - TmX_Reference_List       : named reference lists addressable by code
                                  (tax-haven, land-locked, dual-use, perishable…)
     - TmX_Reference_List_Value : values for those lists
     - TmX_Transaction_Flag     : + Source / Rule_ID / Matched_Value columns
                                  (auto-flag provenance; defaults keep existing
                                   manual rows = 'Manual')

   Auto vs Manual stays on the existing TmX_Flag_Catalogue.Flag_Type_Lkp_ID
   (no change here). Reference country/goods lists already exist
   (REPO_SANCTIONED_COUNTRIES / REPO_SCREENING_GOODS) and are addressed by
   the same Reference_List_Code abstraction in the engine.

   Run:
     sqlcmd -S WAQASKHAN\SQLEXPRESS -d ICBC_DEMO -E -i "<path>\2026_06_040_CreateAutoFlagRuleEngine.sql"
   ==================================================================== */

SET XACT_ABORT ON;
GO

/* ---- TmX_Flag_Rule -------------------------------------------------- */
IF OBJECT_ID(N'dbo.TmX_Flag_Rule', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TmX_Flag_Rule(
        [Rule_ID]             INT IDENTITY(1,1) NOT NULL,
        [Flag_ID]             INT NOT NULL,
        [Rule_Group]          INT NOT NULL CONSTRAINT DF_FlagRule_Group DEFAULT(1),
        [Evaluation_Order]    INT NOT NULL CONSTRAINT DF_FlagRule_Order DEFAULT(1),
        [Logical_Operator]    NVARCHAR(3)   NOT NULL CONSTRAINT DF_FlagRule_Logic DEFAULT('AND'),
        [Left_Source]         NVARCHAR(20)  NOT NULL,
        [Left_Field_Code]     NVARCHAR(100) NULL,
        [Operator]            NVARCHAR(40)  NOT NULL,
        [Right_Operand_Type]  NVARCHAR(20)  NOT NULL,
        [Right_Source]        NVARCHAR(20)  NULL,
        [Right_Field_Code]    NVARCHAR(100) NULL,
        [Static_Value]        NVARCHAR(400) NULL,
        [Reference_List_Code] NVARCHAR(60)  NULL,
        [Aggregate_Spec]      NVARCHAR(MAX) NULL,
        [Handler_Key]         NVARCHAR(60)  NULL,
        [Active_Flag]         BIT NOT NULL CONSTRAINT DF_FlagRule_Active DEFAULT(1),
        [Created_By]          NVARCHAR(100) NULL,
        [Created_Date]        DATETIME NOT NULL CONSTRAINT DF_FlagRule_Created DEFAULT(GETDATE()),
        [Last_Updated_By]     NVARCHAR(100) NULL,
        [Last_Updated_Date]   DATETIME NULL,
        CONSTRAINT XPKTmX_Flag_Rule PRIMARY KEY CLUSTERED ([Rule_ID] ASC),
        CONSTRAINT FK_TmX_Flag_Rule_Flag FOREIGN KEY ([Flag_ID])
            REFERENCES dbo.TmX_Flag_Catalogue ([Flag_ID])
    );

    CREATE NONCLUSTERED INDEX IX_TmX_Flag_Rule_Flag
        ON dbo.TmX_Flag_Rule ([Flag_ID], [Active_Flag])
        INCLUDE ([Rule_Group], [Evaluation_Order]);
END
GO

/* ---- TmX_Reference_List --------------------------------------------- */
IF OBJECT_ID(N'dbo.TmX_Reference_List', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TmX_Reference_List(
        [Reference_List_ID]  INT IDENTITY(1,1) NOT NULL,
        [List_Code]          NVARCHAR(60)  NOT NULL,
        [List_Name]          NVARCHAR(200) NOT NULL,
        [Active_Flag]        BIT NOT NULL CONSTRAINT DF_RefList_Active DEFAULT(1),
        [Created_By]         NVARCHAR(100) NULL,
        [Created_Date]       DATETIME NOT NULL CONSTRAINT DF_RefList_Created DEFAULT(GETDATE()),
        [Last_Updated_By]    NVARCHAR(100) NULL,
        [Last_Updated_Date]  DATETIME NULL,
        CONSTRAINT XPKTmX_Reference_List PRIMARY KEY CLUSTERED ([Reference_List_ID] ASC),
        CONSTRAINT UQ_TmX_Reference_List_Code UNIQUE ([List_Code])
    );
END
GO

/* ---- TmX_Reference_List_Value --------------------------------------- */
IF OBJECT_ID(N'dbo.TmX_Reference_List_Value', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TmX_Reference_List_Value(
        [Reference_List_Value_ID] INT IDENTITY(1,1) NOT NULL,
        [Reference_List_ID]  INT NOT NULL,
        [Value]              NVARCHAR(200) NOT NULL,
        [Value_Type]         NVARCHAR(40)  NULL,
        [Active_Flag]        BIT NOT NULL CONSTRAINT DF_RefListVal_Active DEFAULT(1),
        [Created_By]         NVARCHAR(100) NULL,
        [Created_Date]       DATETIME NOT NULL CONSTRAINT DF_RefListVal_Created DEFAULT(GETDATE()),
        CONSTRAINT XPKTmX_Reference_List_Value PRIMARY KEY CLUSTERED ([Reference_List_Value_ID] ASC),
        CONSTRAINT FK_TmX_Reference_List_Value_List FOREIGN KEY ([Reference_List_ID])
            REFERENCES dbo.TmX_Reference_List ([Reference_List_ID])
    );

    CREATE NONCLUSTERED INDEX IX_TmX_Reference_List_Value_List
        ON dbo.TmX_Reference_List_Value ([Reference_List_ID], [Active_Flag])
        INCLUDE ([Value]);
END
GO

/* ---- TmX_Transaction_Flag — auto-flag provenance (additive) --------- */
IF COL_LENGTH(N'dbo.TmX_Transaction_Flag', N'Source') IS NULL
    ALTER TABLE dbo.TmX_Transaction_Flag
        ADD [Source] NVARCHAR(10) NOT NULL
            CONSTRAINT DF_TmX_Transaction_Flag_Source DEFAULT('Manual');
GO
IF COL_LENGTH(N'dbo.TmX_Transaction_Flag', N'Rule_ID') IS NULL
    ALTER TABLE dbo.TmX_Transaction_Flag ADD [Rule_ID] INT NULL;
GO
IF COL_LENGTH(N'dbo.TmX_Transaction_Flag', N'Matched_Value') IS NULL
    ALTER TABLE dbo.TmX_Transaction_Flag ADD [Matched_Value] NVARCHAR(400) NULL;
GO

PRINT '2026_06_040_CreateAutoFlagRuleEngine.sql applied.';
GO
