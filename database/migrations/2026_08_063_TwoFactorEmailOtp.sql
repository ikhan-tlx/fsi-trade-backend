/* ============================================================================
   2026_08_063_TwoFactorEmailOtp.sql
   ----------------------------------------------------------------------------
   Adds the server-side store for Email 2FA one-time passwords, and seeds the
   Email-OTP tuning config rows into Configuration Management.

   The 2FA on/off + method toggles already exist (2026_07_036):
     TwoFactor.Enabled (Boolean)  |  TwoFactor.Method (Enum: Email/SMS/Totp/None)
   The email SOURCE is the existing SMTP config (Email.Host/Port/From/Username +
   the encrypted Email.Password secret). This migration only adds:
     * TmX_User_Otp — one current, HASHED OTP per (user, purpose) with expiry +
       attempt counter, validated server-side.
     * TwoFactor.Email.OtpLength / OtpExpiryMinutes / MaxAttempts config rows.

   Idempotent + re-runnable. Apply against ICBC_DEMO (or UAT) with sqlcmd.
   ============================================================================ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;   -- required: the TmX_Configuration_* tables carry filtered/computed indexes

/* ---------- 1. Email-OTP store ------------------------------------------- */
IF OBJECT_ID('dbo.TmX_User_Otp','U') IS NULL
BEGIN
    CREATE TABLE dbo.TmX_User_Otp
    (
        Otp_Id            BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TmX_User_Otp PRIMARY KEY,
        User_Id           NVARCHAR(450)  NOT NULL,   -- FK to TmX_User.Id (GUID string)
        Purpose           NVARCHAR(40)   NOT NULL CONSTRAINT DF_TmX_User_Otp_Purpose DEFAULT N'Login',
        Otp_Hash          NVARCHAR(128)  NOT NULL,   -- SHA-256 hex of the code (never the plaintext)
        Expires_At        DATETIME2(3)   NOT NULL,
        Attempts          INT            NOT NULL CONSTRAINT DF_TmX_User_Otp_Attempts DEFAULT 0,
        Max_Attempts      INT            NOT NULL CONSTRAINT DF_TmX_User_Otp_MaxAtt   DEFAULT 5,
        Sent_At           DATETIME2(3)   NOT NULL CONSTRAINT DF_TmX_User_Otp_SentAt   DEFAULT SYSUTCDATETIME(),
        Consumed_At       DATETIME2(3)   NULL,       -- set when successfully verified (spent)
        Created_Date      DATETIME2(3)   NOT NULL CONSTRAINT DF_TmX_User_Otp_Created  DEFAULT SYSUTCDATETIME(),
        Last_Updated_Date DATETIME2(3)   NULL
    );
    -- One current OTP per (user, purpose): send upserts this row.
    CREATE UNIQUE INDEX UX_TmX_User_Otp_User_Purpose
        ON dbo.TmX_User_Otp (User_Id, Purpose);
    PRINT 'Created dbo.TmX_User_Otp';
END
ELSE
    PRINT 'dbo.TmX_User_Otp already exists — skipped.';
GO

/* ---------- 2. Email-OTP tuning config rows (category: security) ---------- */
DECLARE @who nvarchar(100) = N'System (2026_08_063)';
DECLARE @now datetime2(3)  = SYSUTCDATETIME();

-- Definition metadata for the three new keys (Integer, non-secret, editable).
DECLARE @DEF TABLE (K nvarchar(200), Name nvarchar(200), Cat nvarchar(50), Dt nvarchar(20),
                    Def nvarchar(max), Unit nvarchar(40), Rules nvarchar(max), Ord int);
INSERT INTO @DEF (K,Name,Cat,Dt,Def,Unit,Rules,Ord) VALUES
 ('TwoFactor.Email.OtpLength',        N'Email OTP length',       'security','Integer',N'6', N'digits',  N'{"min":4,"max":8,"step":1}',   60),
 ('TwoFactor.Email.OtpExpiryMinutes', N'Email OTP validity',     'security','Integer',N'1', N'minutes', N'{"min":1,"max":30,"step":1}',  70),
 ('TwoFactor.Email.MaxAttempts',      N'Email OTP max attempts', 'security','Integer',N'5', N'attempts',N'{"min":1,"max":10,"step":1}',  80);

-- Insert definitions that don't already exist.
INSERT INTO dbo.TmX_Configuration_Definition
    (Configuration_Key, Display_Name, Description, Category_Key, Data_Type, Default_Value,
     Validation_Rules, Unit, Is_Editable, Is_Sensitive, Requires_Approval, Applies_On_Restart,
     Is_Active, Display_Order, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT d.K, d.Name, NULL, d.Cat, d.Dt, d.Def, d.Rules, d.Unit,
       1, 0, 0, 0, 1, d.Ord, @who, @now, @who, @now
FROM   @DEF d
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Definition x WHERE x.Configuration_Key = d.K);
PRINT CONCAT('Definitions inserted: ', @@ROWCOUNT);

-- Insert the Global active value for each (falls back to Default_Value if absent).
INSERT INTO dbo.TmX_Configuration_Value
    (Configuration_Definition_ID, Configuration_Value, Is_Active, Created_By, Created_Date, Last_Updated_By, Last_Updated_Date)
SELECT def.Configuration_Definition_ID, d.Def, 1, @who, @now, @who, @now
FROM   @DEF d
JOIN   dbo.TmX_Configuration_Definition def ON def.Configuration_Key = d.K
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_Value cv
                   WHERE cv.Configuration_Definition_ID = def.Configuration_Definition_ID
                     AND cv.Branch_ID IS NULL AND cv.Role_ID IS NULL AND cv.Environment IS NULL);
PRINT CONCAT('Global values inserted: ', @@ROWCOUNT);

-- Baseline Create history row per new definition.
INSERT INTO dbo.TmX_Configuration_History
    (Configuration_Definition_ID, Configuration_Value_ID, Old_Value, New_Value, Change_Type, Scope_Descriptor, Changed_By, Changed_Date)
SELECT def.Configuration_Definition_ID, v.Configuration_Value_ID, NULL, v.Configuration_Value, 'Create', 'Global', @who, @now
FROM   @DEF d
JOIN   dbo.TmX_Configuration_Definition def ON def.Configuration_Key = d.K
JOIN   dbo.TmX_Configuration_Value v ON v.Configuration_Definition_ID = def.Configuration_Definition_ID
WHERE  NOT EXISTS (SELECT 1 FROM dbo.TmX_Configuration_History h WHERE h.Configuration_Definition_ID = def.Configuration_Definition_ID);
PRINT CONCAT('Create history rows: ', @@ROWCOUNT);
GO

PRINT '2026_08_063_TwoFactorEmailOtp applied.';
