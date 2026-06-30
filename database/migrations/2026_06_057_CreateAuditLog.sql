-- =========================================================================
-- 2026_06_057_CreateAuditLog.sql
-- 2026-06-24 — generic audit trail. One row per changed entity per save,
-- written by the EF SaveChanges interceptor. Captures who (user + IP),
-- what (entity + key + field old->new JSON), where (screen / request path +
-- method) and when. Idempotent. Run on: ICBC_DEMO
-- =========================================================================
USE [ICBC_DEMO];
GO
SET NOCOUNT ON;
IF OBJECT_ID('dbo.TmX_Audit_Log','U') IS NULL
BEGIN
    CREATE TABLE dbo.TmX_Audit_Log (
        Audit_Id     BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TmX_Audit_Log PRIMARY KEY,
        Tenant_Id    INT            NULL,
        User_Id      NVARCHAR(100)  NULL,
        User_Name    NVARCHAR(200)  NULL,
        Ip_Address   NVARCHAR(64)   NULL,
        Action       NVARCHAR(20)   NOT NULL,   -- Insert / Update / Delete
        Entity_Type  NVARCHAR(200)  NOT NULL,
        Entity_Key   NVARCHAR(200)  NULL,
        Changes_Json NVARCHAR(MAX)  NULL,        -- [{ "field":..,"old":..,"new":.. }]
        Screen       NVARCHAR(200)  NULL,        -- FE-supplied X-Screen header
        Request_Path NVARCHAR(400)  NULL,
        Http_Method  NVARCHAR(10)   NULL,
        Created_Date DATETIME2(3)   NOT NULL CONSTRAINT DF_TmX_Audit_Log_Created DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_TmX_Audit_Log_Created ON dbo.TmX_Audit_Log(Created_Date DESC);
    CREATE INDEX IX_TmX_Audit_Log_User    ON dbo.TmX_Audit_Log(User_Name);
    CREATE INDEX IX_TmX_Audit_Log_Entity  ON dbo.TmX_Audit_Log(Entity_Type);
    PRINT 'Created dbo.TmX_Audit_Log';
END
ELSE PRINT 'TmX_Audit_Log already exists — no-op';
GO
