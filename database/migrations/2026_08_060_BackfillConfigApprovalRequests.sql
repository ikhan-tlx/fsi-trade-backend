-- =========================================================================
-- 2026_08_060_BackfillConfigApprovalRequests.sql
-- -------------------------------------------------------------------------
-- One-off backfill for approval-gated config changes that were parked BEFORE
-- the maker/checker wiring existed. Prior to type 0011, saving a setting with
-- Requires_Approval=1 only parked Pending_Value (a Phase-2 TODO) and NEVER
-- created a TmX_Request — so the change sat "awaiting approval" with nothing to
-- approve in the Requests screen (e.g. the "Access token lifetime" change).
--
-- For every ACTIVE global config value that still has a non-null Pending_Value
-- on an approval-gated + active definition, and has no pending 0011 request yet,
-- synthesize the exact request the new save path would have created. Entity_Json
-- matches ApplyConfigChangeCommand's shape (key + value are bound; displayName /
-- dataType / *Display are display-only). Secrets are masked in the display
-- fields; `value` carries the stored (already-encrypted for secrets) form so the
-- approve-time replay round-trips it without re-encrypting.
--
-- Idempotent: guarded by NOT EXISTS on an existing pending 0011 for the key.
-- Depends on 2026_08_059 (type 0011 must exist for the FK).
--
-- Run:
--   sqlcmd -S "WAQASKHAN\SQLEXPRESS" -d ICBC_DEMO -E -i "<path>\2026_08_060_BackfillConfigApprovalRequests.sql"
-- =========================================================================

USE [ICBC_DEMO];
GO

SET NOCOUNT ON;
GO

INSERT INTO dbo.TmX_Request
    (Tenant_Id, Request_Type_Code, Entity_Status_Code, Request_Status_Code,
     Entity_Id, Entity_Json, Command_Key, Created_By, Created_Date)
SELECT
    1, N'0011', N'0002', N'0011',
    d.Configuration_Key,
    N'{"key":"' + STRING_ESCAPE(d.Configuration_Key, 'json')
      + N'","value":'       + CASE WHEN v.Pending_Value IS NULL THEN N'null' ELSE N'"' + STRING_ESCAPE(v.Pending_Value, 'json') + N'"' END
      + N',"oldValue":'     + CASE WHEN v.Configuration_Value IS NULL THEN N'null' ELSE N'"' + STRING_ESCAPE(v.Configuration_Value, 'json') + N'"' END
      + N',"displayName":"' + STRING_ESCAPE(ISNULL(d.Display_Name, d.Configuration_Key), 'json')
      + N'","dataType":"'   + STRING_ESCAPE(ISNULL(d.Data_Type, N''), 'json')
      + N'","newDisplay":'  + CASE WHEN d.Is_Sensitive = 1 THEN N'"••••••"'
                                   WHEN v.Pending_Value IS NULL THEN N'null'
                                   ELSE N'"' + STRING_ESCAPE(v.Pending_Value, 'json') + N'"' END
      + N',"oldDisplay":'   + CASE WHEN d.Is_Sensitive = 1 THEN N'"••••••"'
                                   WHEN v.Configuration_Value IS NULL THEN N'null'
                                   ELSE N'"' + STRING_ESCAPE(v.Configuration_Value, 'json') + N'"' END
      + N'}',
    N'0011:0002', N'system-backfill', SYSUTCDATETIME()
FROM dbo.TmX_Configuration_Value v
JOIN dbo.TmX_Configuration_Definition d
     ON d.Configuration_Definition_ID = v.Configuration_Definition_ID
WHERE v.Is_Active = 1 AND v.Branch_ID IS NULL AND v.Role_ID IS NULL AND v.Environment IS NULL
  AND v.Pending_Value IS NOT NULL
  AND d.Requires_Approval = 1 AND d.Is_Active = 1
  AND NOT EXISTS (
        SELECT 1 FROM dbo.TmX_Request r
        WHERE r.Request_Type_Code   = N'0011'
          AND r.Entity_Id           = d.Configuration_Key
          AND r.Request_Status_Code = N'0011');

PRINT CONCAT('2026_08_060 complete - backfilled ', @@ROWCOUNT, ' orphaned approval-gated config pending(s) into TmX_Request.');
GO
