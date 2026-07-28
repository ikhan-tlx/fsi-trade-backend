# OFSAA / Oracle migrations

Schema changes that apply to the **OFSAA (Oracle) database** — the FCCM / ECM side the app reaches
through the `FccmOracleConnection` (see `appsettings.json`) — kept **separate** from the SQL Server
application migrations in `../migrations/`.

> **Target database:** Oracle (OFSAA — `MANTAS` schema; the app connects as `ofsaafccm`, which
> resolves the OFSAA objects via synonyms). **NOT** the SQL Server `ICBC_DEMO` application database.
> Never run these against SQL Server, and never run the `../migrations/` (SQL Server) scripts against
> Oracle.

## How to run

Connect with a principal that has `ALTER`/`CREATE` on the target objects — either the app's OFSAA
login schema (`ofsaafccm`) or the owning schema (`MANTAS`) — using SQLcl or SQL*Plus, e.g.:

```
sql ofsaafccm/<pwd>@<host>:1521/<service>  @2026_07_001_TmxTransFromRepo_CorrelationColumns.sql
```

Objects are referenced **unqualified** (as the running app references them), so they resolve against
the login schema / its synonyms — the same way the proven dev provisioning ran. If you execute as a
DBA outside that schema, qualify the object with its owner (`MANTAS.<object>`).

## Conventions

- **File name:** `YYYY_MM_0NN_Description.sql` (ordered by the numeric prefix). OFSAA has its **own**
  sequence, independent of the SQL Server `../migrations/` numbering.
- **Idempotent:** every statement is wrapped to swallow the "already exists / already done" Oracle
  errors (`-1430` column exists, `-955` object exists, `-942` / `-904` where a guard is appropriate),
  so a script is safe to re-run and safe to include in every environment's deploy.
- **One logical change per file**; a short header comment stating the symptom, root cause and the
  environment(s) it targets.
- **UTC / no data mutation** unless the change is explicitly a data fix.

## Relationship to `FCCM_Oracle_Atomic_DDL.sql`

`../FCCM_Oracle_Atomic_DDL.sql` is the **full baseline** OFSAA provisioning (all FCCM tables +
`PR_CREATE_EVENT_DEMO[_CFG]`) used to stand up a fresh environment. **Incremental** OFSAA changes from
now on live here as numbered migrations. (Future cleanup: the atomic-DDL's addendum sections can be
folded into this folder so there is a single ordered OFSAA change history.)

## Migrations

| # | File | Purpose |
|---|---|---|
| 2026_07_001 | `2026_07_001_TmxTransFromRepo_CorrelationColumns.sql` | Add the app-written correlation columns (`CUSTOMERNAME`, `CLIENTREFNUM`, `TRANSACTIONO`) to `TMX_TRANS_FROM_REPO` — fixes `ORA-00904` on "Proceed to FCCM" where the base OFSAA table lacks them (UAT). |
