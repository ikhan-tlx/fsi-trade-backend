# Schema Reference — Source of Truth

**Read this file before adding any DB-touching code.** That means: a new entity, a
new EF configuration, a new LINQ query against an existing table, or any
migration script. Skipping this step has cost us round-trips in the past
(v21 `WorkflowInbox.AddingDate` doesn't exist, `WorkflowProcessScheme.SchemeCode`
not `Code`, `WorkflowInbox.IdentityId` is `uniqueidentifier`, etc.).

---

## 1. Where the schema lives

The canonical DDL for the deployed database is at:

```
D:\ICBC - Latest\ICBC_DEMO-Schema.sql
```

It's a full SSMS-generated DDL export (CREATE TABLE / CREATE VIEW / indexes /
constraints) for the `ICBC_DEMO` database as it exists on the FSI side. Date of
last export is in the file header (`Script Date: 5/11/2026 ...`). Refresh the
export when:

- A migration script lands on the FSI side that the new backend should react to.
- Anyone reports column / type mismatches between the entity and live data.
- We start a new slice that touches tables we haven't mapped yet.

---

## 2. Discipline before code

Before defining a new Domain entity or writing a new LINQ query, do this:

1. **Grep the schema file** for the table or view name:
   ```text
   CREATE TABLE [dbo].[<Table_Name>]
   CREATE VIEW [dbo].[<View_Name>]
   ```
2. **Read the column list verbatim**. Note nullability and types — particularly
   for `uniqueidentifier` vs `nvarchar`, `int` vs `bigint`, and presence/absence
   of audit columns (`Created_Date`, `Last_Updated_Date`, etc.).
3. **Cross-check against any legacy EF6 boilerplate** in
   `FSI.Trade.Application/.../Persistence/Entities/`. The legacy entities can
   drift from the live schema — when they disagree, **schema wins**.
4. **If the table is a view**, read the `SELECT` body too. Views can collapse
   multiple inbox rows (e.g. `TmX_Transaction_VW.Inbox_User_ID` goes NULL when
   `Total_Rows > 1` — "Multiple Users" bucket assignment). Filtering or
   ordering decisions depend on the view's actual shape, not its column list.
5. **Only then** write the entity / config / handler.

---

## 3. What this file is NOT

This is not a hand-maintained catalogue of every column we care about. That
catalogue lives in [`DB_ENTITY_USAGE.md`](./DB_ENTITY_USAGE.md) — it tracks
*which* tables the new backend touches and *how*. This file is just a pointer
plus the discipline rule for using the canonical schema export.

---

## 4. Examples of what we've caught using this discipline

- `WorkflowInbox.AddingDate` doesn't exist in v21 (legacy assumed it did) →
  found by grepping the schema before mapping; saved a runtime failure.
- `WorkflowProcessScheme.SchemeCode` not `Code` → v21 column rename, caught
  pre-build.
- `WorkflowInbox.IdentityId` is `uniqueidentifier`, not `nvarchar` → caught
  before the JOIN was written; handler does `Guid.TryParse(userId, ...)`.
- `TmX_Transaction_VW` collapses inbox rows when more than one actor is
  assigned and sets `Inbox_User_ID = NULL` — drove the design of Slice 6's
  list endpoint (unassigned-but-open transactions show up naturally).
- `TmX_User.User_ID` is `nvarchar(50)` storing a GUID-shaped string. This led
  to the `User_Identifier` cleanup item in BACKLOG.md — JOIN performance is
  paid on every authenticated request.

---

## 5. If you need to share a schema-related decision with stakeholders

Don't paste the schema file or column extracts into chat / email — that
violates the "single source of truth" rule. Instead, cite the file path plus
the table name. Anyone with repo access can grep it themselves.

---

*Last updated: 2026-05-11 (Slice 6 kickoff).*
