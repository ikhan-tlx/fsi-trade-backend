/* ============================================================================
   2026_07_003_FixPasswordChangeAuditSourceIdUniqueFilter
   ----------------------------------------------------------------------------
   Bug: dbo.Password_Change_Audit_Trail has a plain UNIQUE key on
   Source_Audit_Trail_ID (UQ_Password_Change_Audit_Trail_SourceId). That column
   is ONLY populated for rows migrated from the old AspNet audit trail; every
   NEW password change (change-password, admin reset, expired-password reset)
   inserts Source_Audit_Trail_ID = NULL.

   SQL Server treats NULL as a single distinct value in a UNIQUE index, so the
   SECOND new password-change audit row fails with:
     "Violation of UNIQUE KEY constraint ... duplicate key value is (<NULL>)"
   which surfaced as a 500 on /api/v1/Auth/ResetExpiredPassword.

   Fix: replace the plain UNIQUE constraint/index with a FILTERED unique index
   that only enforces uniqueness for non-NULL Source_Audit_Trail_ID (preserving
   the intended de-dupe of migrated rows while allowing many NULLs).

   Forward-only, idempotent.
   ============================================================================ */

SET NOCOUNT ON;
-- Filtered indexes require these SET options ON at CREATE time (sqlcmd defaults
-- QUOTED_IDENTIFIER OFF). Set them here so the script is self-contained.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- Drop the offending uniqueness, whether it exists as a constraint or an index.
IF EXISTS (SELECT 1 FROM sys.key_constraints
           WHERE name = 'UQ_Password_Change_Audit_Trail_SourceId'
             AND parent_object_id = OBJECT_ID('dbo.Password_Change_Audit_Trail'))
    ALTER TABLE dbo.Password_Change_Audit_Trail
        DROP CONSTRAINT UQ_Password_Change_Audit_Trail_SourceId;
GO

IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE name = 'UQ_Password_Change_Audit_Trail_SourceId'
             AND object_id = OBJECT_ID('dbo.Password_Change_Audit_Trail'))
    DROP INDEX UQ_Password_Change_Audit_Trail_SourceId ON dbo.Password_Change_Audit_Trail;
GO

-- Recreate as a filtered unique index (NULLs excluded → multiple new rows OK,
-- migrated rows with a real Source_Audit_Trail_ID stay unique).
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UX_Password_Change_Audit_Trail_SourceId_NotNull'
                 AND object_id = OBJECT_ID('dbo.Password_Change_Audit_Trail'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_Password_Change_Audit_Trail_SourceId_NotNull
        ON dbo.Password_Change_Audit_Trail (Source_Audit_Trail_ID)
        WHERE Source_Audit_Trail_ID IS NOT NULL;
GO
