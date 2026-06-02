-- =========================================================================
-- 2026_05_027_CreateTransactionDocumentBridge.sql
--
-- Slice 10 §7.4 (reworked) — bridge table linking transactions to documents.
--
-- Why a bridge instead of an FK on TmX_Document
-- ---------------------------------------------
-- User direction (May 2026): "Instead of adding the Transaction_id directly
-- into the tmx_document make a bridge table to have transaction id and
-- document id. so the document table won't be bind to single entity".
--
-- This keeps TmX_Document polymorphic — the same file can serve as
-- flag evidence, a transaction attachment, a customer document, etc.,
-- without TmX_Document carrying a fan-out of nullable FKs (one per
-- consumer module). Each consumer owns its own linkage:
--
--   TmX_Transaction_Document   — Slice 10 §7.4 (THIS migration; many-to-many)
--   TmX_Transaction_Flag       — Slice 8 (one-to-one via Evidence_Document_ID)
--   (future)                   — customer docs, report attachments, etc.
--
-- Schema
-- ------
-- Transaction_Document_ID    int IDENTITY PK
-- Transaction_ID             int     NOT NULL  → FK to TmX_Transaction
-- Document_ID                int     NOT NULL  → FK to TmX_Document
-- Tenant_ID                  int     NOT NULL
-- Active_Flag                bit     NOT NULL
-- Created_By                 nvarchar(100) NOT NULL
-- Created_Date               datetime NOT NULL
-- Last_Updated_By            nvarchar(100) NULL
-- Last_Updated_Date          datetime NULL
--
-- Unique constraint on (Transaction_ID, Document_ID) — same document can be
-- attached to multiple transactions, but a given (txn, doc) pair appears
-- at most once. ON DELETE CASCADE on Transaction_ID — when a transaction
-- is deleted, the linkage rows go with it (the document file itself stays
-- intact; cleanup is a separate concern).
--
-- Idempotent: every CREATE / ADD is guarded. Safe to re-run.
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
-- 1. Create the bridge table.
-- -------------------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1
    FROM   sys.objects
    WHERE  object_id = OBJECT_ID(N'dbo.TmX_Transaction_Document')
      AND  type      = N'U'
)
BEGIN
    CREATE TABLE [dbo].[TmX_Transaction_Document](
        [Transaction_Document_ID] [int]            IDENTITY(1,1) NOT NULL,
        [Transaction_ID]          [int]            NOT NULL,
        [Document_ID]             [int]            NOT NULL,
        [Tenant_ID]               [int]            NOT NULL,
        [Active_Flag]             [bit]            NOT NULL CONSTRAINT [DF_TmX_Transaction_Document_Active] DEFAULT (1),
        [Created_By]              [nvarchar](100)  NOT NULL,
        [Created_Date]            [datetime]       NOT NULL,
        [Last_Updated_By]         [nvarchar](100)  NULL,
        [Last_Updated_Date]       [datetime]       NULL,
        CONSTRAINT [PK_TmX_Transaction_Document] PRIMARY KEY CLUSTERED ([Transaction_Document_ID] ASC)
    );
    PRINT '  Created table TmX_Transaction_Document.';
END
ELSE
    PRINT '  Table TmX_Transaction_Document already exists — skipped.';
GO

-- -------------------------------------------------------------------------
-- 2. FK → TmX_Transaction. ON DELETE CASCADE so linkage rows disappear with
--    the transaction; the underlying document remains.
-- -------------------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_TmX_Transaction_Document_Transaction')
BEGIN
    ALTER TABLE [dbo].[TmX_Transaction_Document]
        ADD CONSTRAINT [FK_TmX_Transaction_Document_Transaction]
            FOREIGN KEY ([Transaction_ID])
            REFERENCES [dbo].[TmX_Transaction] ([Transaction_ID])
            ON DELETE CASCADE;
    PRINT '  Added FK_TmX_Transaction_Document_Transaction.';
END
GO

-- -------------------------------------------------------------------------
-- 3. FK → TmX_Document. NO CASCADE — documents survive transaction deletion
--    (other consumers may still reference them). Manual cleanup if/when
--    needed.
-- -------------------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_TmX_Transaction_Document_Document')
BEGIN
    ALTER TABLE [dbo].[TmX_Transaction_Document]
        ADD CONSTRAINT [FK_TmX_Transaction_Document_Document]
            FOREIGN KEY ([Document_ID])
            REFERENCES [dbo].[TmX_Document] ([Document_ID]);
    PRINT '  Added FK_TmX_Transaction_Document_Document.';
END
GO

-- -------------------------------------------------------------------------
-- 4. Uniqueness — a (transaction, document) pair appears once.
-- -------------------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1
    FROM   sys.indexes
    WHERE  name      = N'UX_TmX_Transaction_Document_TxnDoc'
      AND  object_id = OBJECT_ID(N'dbo.TmX_Transaction_Document'))
BEGIN
    CREATE UNIQUE INDEX [UX_TmX_Transaction_Document_TxnDoc]
        ON [dbo].[TmX_Transaction_Document] ([Transaction_ID], [Document_ID]);
    PRINT '  Created UX_TmX_Transaction_Document_TxnDoc unique index.';
END
GO

-- -------------------------------------------------------------------------
-- 5. Lookup index — the list endpoint scans by Transaction_ID, ordered by
--    Created_Date DESC.
-- -------------------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1
    FROM   sys.indexes
    WHERE  name      = N'IX_TmX_Transaction_Document_Transaction_ID_Created_Date'
      AND  object_id = OBJECT_ID(N'dbo.TmX_Transaction_Document'))
BEGIN
    CREATE INDEX [IX_TmX_Transaction_Document_Transaction_ID_Created_Date]
        ON [dbo].[TmX_Transaction_Document] ([Transaction_ID], [Created_Date] DESC)
        INCLUDE ([Document_ID], [Active_Flag]);
    PRINT '  Created IX_TmX_Transaction_Document_Transaction_ID_Created_Date.';
END
GO

PRINT 'Migration 2026_05_027_CreateTransactionDocumentBridge — done.';
GO
