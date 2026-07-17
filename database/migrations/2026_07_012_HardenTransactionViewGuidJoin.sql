/* ============================================================================
   2026_07_012_HardenTransactionViewGuidJoin
   ----------------------------------------------------------------------------
   Hardening: dbo.TmX_Transaction_VW joins TmX_User.User_ID (nvarchar) against
   WorkflowInbox.IdentityId (uniqueidentifier):

       LEFT OUTER JOIN dbo.TmX_User AS inboxUser
              ON inboxUser.User_ID = wfInbox.IdentityId

   SQL Server converts EVERY User_ID to uniqueidentifier to evaluate that join,
   so a single non-GUID User_ID throws "Conversion failed when converting from a
   character string to uniqueidentifier" and 500s the ENTIRE transaction list +
   stats (any query over the view). We now enforce GUID reviewer ids, but make
   the view itself convert-safe so a stray non-GUID account can never take the
   grid down again: TRY_CONVERT yields NULL (no match) instead of throwing.

   Patches the LIVE definition surgically (OBJECT_DEFINITION -> targeted REPLACE
   -> ALTER VIEW) so nothing else in the view body changes. Idempotent: skips if
   the pattern is absent or already hardened. Behaviour is identical for valid
   GUID ids (same GUID comparison); only non-GUID ids stop erroring.
   ============================================================================ */

SET NOCOUNT ON;
GO

DECLARE @def NVARCHAR(MAX) = OBJECT_DEFINITION(OBJECT_ID('dbo.TmX_Transaction_VW'));

IF @def IS NULL
    PRINT 'TmX_Transaction_VW not found; skipping.';
ELSE IF @def LIKE '%TRY_CONVERT(uniqueidentifier, inboxUser.User_ID)%'
    PRINT 'TmX_Transaction_VW already hardened; no change.';
ELSE IF @def NOT LIKE '%inboxUser.User_ID = wfInbox.IdentityId%'
    PRINT 'TmX_Transaction_VW: expected join pattern not found; no change (review manually).';
ELSE
BEGIN
    SET @def = REPLACE(@def, 'CREATE VIEW', 'ALTER VIEW');
    SET @def = REPLACE(@def,
        'inboxUser.User_ID = wfInbox.IdentityId',
        'TRY_CONVERT(uniqueidentifier, inboxUser.User_ID) = wfInbox.IdentityId');
    EXEC sp_executesql @def;
    PRINT 'TmX_Transaction_VW: inbox-user join hardened with TRY_CONVERT.';
END
GO
