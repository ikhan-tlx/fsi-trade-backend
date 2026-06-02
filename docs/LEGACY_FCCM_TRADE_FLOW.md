# Legacy FCCM Trade Submission Flow

Reference spec for the Slice 9 port. Captures the **exact** Oracle /
SQL Server / HTTP operations the legacy
`ICBC_Integration_Test.TradeController` performs when a finalised
trade transaction is submitted to FCCM, so the new backend can be
verified line-by-line against legacy behaviour.

**Scope**: Trade flow only (non-KYC products — ProductId ≠ 5). The
KYC flow is referenced for context where relevant; full KYC details
are in `LEGACY_TRANSACTION_FLOW.md` Phase 4.

**Source**: `D:\ICBC - Latest\Integration KYC\Controllers\TradeController.cs`
+ `Repositories\ModelContext.cs` (Sept 2025).

---

## 0. Where the data actually lives

**Critical clarification**: every database operation in this flow
(except the local SQL Server status fallback) targets a **single
Oracle DB** — the FCCM Oracle instance referenced by
`ConnectionStrings["FCCMDB"]`. The EF-styled staging entities in
`Integration KYC/Repositories/*.cs` look like SQL Server but actually
map to Oracle tables in the `MANTAS` schema:

```csharp
// ModelContext.cs:36
optionsBuilder.UseOracle(ConfigurationManager.ConnectionStrings["FCCMDB"].ConnectionString);

// ModelContext.cs:44 — Oracle schema
modelBuilder.HasAnnotation("Relational:DefaultSchema", "MANTAS");

// ModelContext.cs:51
entity.ToTable("BENEFICIARY_DETAIL_STAGING");           // UPPERCASE Oracle
entity.Property(e => e.MessageId)
    .HasColumnName("MESSAGE_ID")                        // UPPERCASE Oracle
    .HasMaxLength(300).IsUnicode(false);
```

So **no separate SQL Server staging DB exists**. The four staging
"tables" `BeneficiaryDetailStaging` / `LcTrnDetStaging` /
`CustMasterStaging` / `EventStaging` are Oracle tables in `MANTAS`
schema. Two more staging entities the legacy declares but I missed in
my initial pass:

- `CustBankingStaging` (banking accounts)
- `RtCaseTracking` (message-id correlation log)

Plus four Oracle FCC tables the legacy mutates directly via ADO.NET
(no EF mapping):

- `RT_CASE_TRACKING` (truncate + insert each run)
- `EVENT_STAGING` (truncated each run — this is the Oracle one, distinct from `MANTAS.EVENT_STAGING`? Most likely the same in the default schema. The TRUNCATE doesn't specify schema, defaults to login schema.)
- `KDD_CASE_CUSTOMERS` (FCCM case-customer table)
- `KDD_CASES` (FCCM case master)
- `TMX_TRANS_FROM_REPO` (correlation table — created by the bank, not FCCM OOB)

---

## 1. Entry point + branch on ProductId

```csharp
// caseInsertionController.Post(RepoPayloadModel jsondata)
if (jsondata.ProductId == 5)
    /* KYC flow — see LEGACY_TRANSACTION_FLOW.md Phase 4 */
else
    return TradeController.TradeTransactionToFCCM(jsondata);
```

Only **non-KYC products** (Import LC = 2, Export LC = 3, LG = 6, etc.)
hit the trade flow documented below. KYC has its own pipeline already
ported in Slice 4.

---

## 2. TradeTransactionToFCCM — full operation sequence

The legacy method orchestrates **two phases**: pre-flag-check setup +
conditional FCCM submission.

### Phase A — setup + staging writes (runs every submit)

| # | Operation | Type | Purpose |
|---|---|---|---|
| 1 | `TRUNCATE TABLE RT_CASE_TRACKING` | Oracle ADO.NET (`ExecuteNonQuery`) | Clear correlation log from prior runs. Note: TRUNCATE not DELETE — fast, no row-level WAL |
| 2 | `TRUNCATE TABLE EVENT_STAGING` | Oracle ADO.NET (`ExecuteNonQuery`) | Clear prior flag events. Same rationale |
| 3 | `SELECT MESSAGE_ID_SEQ.nextval FROM DUAL` | Oracle ADO.NET (`ExecuteReader`) | Get next MessageId from Oracle sequence (NOT a Guid — legacy uses Oracle sequence). Read as string |
| 4 | `INSERT INTO RT_CASE_TRACKING (MESSAGE_ID) VALUES (?)` | Oracle ADO.NET | Log the new MessageId for correlation |
| 5 | `BeneficiaryDetailMapping(repoTransaction, MessageId)` | EF Add (Oracle) | Build one row + add to ModelContext.BeneficiaryDetailStaging |
| 6 | `LcTrnDetStagingMapping(repoTransaction, MessageId)` | EF Add (Oracle) | Build one row + add to ModelContext.LcTrnDetStaging (the ~330-col table) |
| 7 | `CustMasterStagingMapping(repoTransaction, MessageId)` | EF Add (Oracle) | Build one row + add to ModelContext.CustMasterStaging |
| 8 | `stagingcontext.SaveChanges()` | EF (Oracle) | Commit all three staging rows in one transaction |
| 9 | `PopulateRedflags_Event_Staging(MessageId, ClientRef, "Customer", customerName, "Customer Details: TF12345")` | EF Add + SaveChanges (Oracle) | One "customer details" event row in EventStaging |
| 10..N | For each MRL slot 1..~27: if any product-prefixed `*MRLn == 1`, `PopulateRedflags_Event_Staging(MessageId, ClientRef, "Customer", "Manual Redflag", "<long indicator text hardcoded>")` | EF Add + SaveChanges (Oracle) | One event row per ticked logical flag. Increments `redflagsCount` |

After Phase A, the legacy has written:

- 3 staging rows (Beneficiary + LcTrnDet + CustMaster)
- 1 customer info event in EventStaging
- N flag events in EventStaging (one per ticked logical flag)
- 1 row in RT_CASE_TRACKING (the correlation log)

### Phase B — conditional FCCM case creation

The branch is on `redflagsCount > 0`:

```csharp
if (redflagsCount > 0)
{
    GetRepoDetails(repoTransaction);                          // op 11
    string caseId = Execute_PR_CREATE_EVENT_DEMO_PROC(MessageId, "FT");  // op 12 (the SP)
    UpdateCustomerDetailsInFCCM(repoTransaction, caseId);     // ops 13 + 14
    return caseId;
}
else
{
    // No flags ticked → no FCCM case. Local-only status update.
    using (SqlConnection con = new SqlConnection(...))
    {
        new SqlCommand($"UPDATE TmX_Transaction SET Transaction_Status_Lkp = 3059 " +
                       $"WHERE Transaction_Id = {transactionId}", con).ExecuteNonQuery();
    }
    return "CA-NOHIT";
}
```

**Key insight**: if no flags were ticked, FCCM is NOT contacted at
all. The transaction's local status flips to lookup id 3059 (presumed
"No Risk / No FCCM Case") and the function returns the sentinel
`"CA-NOHIT"`.

| # | Operation | Type | Purpose | Condition |
|---|---|---|---|---|
| 11 | `INSERT INTO TMX_TRANS_FROM_REPO (CUSTOMERNAME, CLIENTREFNUM, TRANSACTION_ID, TRANSACTIONO) VALUES (?, ?, ?, ?)` | Oracle ADO.NET | Correlation row — links transaction to its eventual FCCM case | Only if `redflagsCount > 0` |
| 12 | `EXEC PR_CREATE_EVENT_DEMO(MESSAGE_ID, PRODUCT="FT", NUM=1, RESID OUT)` | Oracle ADO.NET (SP with OUT param) | The SP reads RT_CASE_TRACKING + EVENT_STAGING + MANTAS.* staging by MessageId, builds the FCCM case, returns case id | Only if `redflagsCount > 0` |
| 13 | `INSERT INTO KDD_CASE_CUSTOMERS (FULL_NM, CUST_INTRL_ID, JRSDCN_CD, CASE_CUST_SEQ_ID, CASE_INTRL_ID, CUST_TYPE_CD, BUS_DMN_LIST_TX) VALUES (?, ?, 'PAK', ?, ?, 'ORG', 'abc')` | Oracle ADO.NET | Post-SP enrichment — attach customer to the just-created case | Only if `redflagsCount > 0` |
| 14 | `UPDATE KDD_CASES SET CASE_TITL_NM = ? WHERE CASE_INTRL_ID = ?` | Oracle ADO.NET | Post-SP enrichment — set the case title to the transaction number | Only if `redflagsCount > 0` |
| (15) | `UPDATE TmX_Transaction SET Transaction_Status_Lkp = 3059 WHERE Transaction_Id = ?` | **SQL Server ADO.NET** | No-FCCM-case status flip on the local ICBC DB | Only if `redflagsCount == 0` |

**Hardcoded `"FT"`** at op 12 — the legacy passes literal `"FT"`
(Foreign Trade) as the Product parameter, NOT the transaction's
actual product code. The SP uses this to route to the trade
event-builder logic.

---

## 3. Side notes — KYC flow (for context)

Slice 4 already ported this. Documenting here for completeness because
Option A's coverage check should include the existing Oracle reads
from `FccmOracleReader`:

| Op | Source | Purpose |
|---|---|---|
| `SELECT CASE_ID FROM FCC_OB_RA WHERE REQUEST_ID = ?` | caseInsertionController.cs:255 | 20-sec poll loop for case id after HTTP submit |
| `UPDATE KDD_CASES SET PRIORITY_CD = (CASE WHEN ... )` | caseInsertionController.cs:298 | Map RISK_CATEGORY_KEY → priority code |
| `UPDATE KDD_CASE_CUSTOMERS SET FULL_NM = (SELECT FROM fcc_ob_cust)` | caseInsertionController.cs:326 | Sync customer name from FCCM observation |
| Various ExecuteNonQuery in `updateTransactionStatusAPIController.cs` | The callback handler | Update transaction status on FCCM callback |

All ADO.NET on the same `FCCMDB` connection.

---

## 4. Option A coverage check

Each legacy operation → Option A (Oracle.ManagedDataAccess.Core only,
no Oracle.EntityFrameworkCore) implementation approach:

| Legacy op | ADO.NET pattern | Notes |
|---|---|---|
| TRUNCATE TABLE | `cmd.CommandText = "TRUNCATE TABLE X"; await cmd.ExecuteNonQueryAsync();` | Trivial |
| Sequence read | `cmd.CommandText = "SELECT X_SEQ.nextval FROM DUAL"; (string)await cmd.ExecuteScalarAsync();` | Trivial |
| INSERT staging row | Parameterised `INSERT INTO MANTAS.X (col1, col2, ...) VALUES (:p1, :p2, ...)` with `cmd.Parameters.Add(":p1", OracleDbType.Varchar2).Value = ...;` per column | Verbose but mechanical; builder helper trims boilerplate |
| INSERT N event rows | Loop the INSERT N times in one connection. Could batch via `OracleCommand.ArrayBindCount` later if N gets large | Today max ~30 rows per submit — fine sequential |
| Stored procedure with OUT param | `cmd.CommandText = "PR_X"; cmd.CommandType = StoredProcedure;` + IN params + OUT param with `.Direction = Output;` + read after `ExecuteNonQueryAsync` | **EF Core doesn't model OUT params natively** — this is where EF would lose vs raw ADO.NET anyway |
| UPDATE | Parameterised UPDATE | Trivial |
| Local SQL Server status flip | Already in `ApplicationDbContext` — we use `_db.SaveChangesAsync()` | No change |

**No legacy operation requires EF.** Specifically:

| What you might think you need EF for | Why you don't |
|---|---|
| Change tracking | Submit pipeline is write-only — no fetch-modify-save cycles |
| Migrations | FCCM owns the schema — we never migrate it |
| LINQ projections | No app-side queries against Oracle (single sequence read is trivial ADO.NET) |
| Connection pooling | Oracle.ManagedDataAccess.Core has built-in pooling |
| Automatic parameter binding | ~3 lines of ADO.NET per param — trivial |
| Lazy loading / nav properties | No related entities — flat INSERTs only |

**What we lose without EF**: nothing material for this workload.
**What we gain**: ~25 MB lighter deploy, all Oracle in one service
file, no provider-specific EF quirks (Oracle EF Core has known issues
with Identity columns vs sequences, batch INSERTs, and some
nullable-column edge cases).

---

## 5. Decision matrix vs other options

| Option | NuGets | Lines of code we write | Lines of code we delete | Provider quirks risk |
|---|---|---|---|---|
| **A** — ADO.NET only | Oracle.ManagedDataAccess.Core (~5 MB) | ~250 (one method per Oracle op) | Phase 2 SQL Server FccmStagingDbContext + 4 EF configs (~400 lines) | Low |
| B — EF for staging + ADO.NET for rest | Oracle.ManagedDataAccess.Core + Oracle.EntityFrameworkCore (~30 MB) | ~400 (rewrite DbContext for Oracle + EF configs with uppercase + ADO.NET ops for non-EF) | Same as A | Medium (Oracle EF Core's edge cases) |
| C — EF for everything | Both NuGets | ~600 (model RT_CASE_TRACKING, KDD_CASE_CUSTOMERS, KDD_CASES, etc. as EF too; still need raw ADO.NET for the SP OUT param) | Same as A | Medium |

---

## 6. Operations Option A must implement

Final inventory — what `FccmOracleReader` needs after the rework:

```csharp
// Sequence + tracking
Task<string> GetNextMessageIdAsync(CancellationToken ct);                              // SELECT MESSAGE_ID_SEQ.nextval
Task TruncateRtCaseTrackingAsync(CancellationToken ct);                                // TRUNCATE TABLE RT_CASE_TRACKING
Task TruncateEventStagingAsync(CancellationToken ct);                                  // TRUNCATE TABLE EVENT_STAGING
Task InsertRtCaseTrackingAsync(string messageId, CancellationToken ct);                // INSERT INTO RT_CASE_TRACKING

// Staging INSERTs (Oracle MANTAS schema)
Task InsertBeneficiaryDetailStagingAsync(BeneficiaryDetailStaging row, CancellationToken ct);
Task InsertLcTrnDetStagingAsync(LcTrnDetStaging row, CancellationToken ct);
Task InsertCustMasterStagingAsync(CustMasterStaging row, CancellationToken ct);
Task InsertEventStagingAsync(EventStaging row, CancellationToken ct);                  // Called N+1 times per submit

// SP + post-SP Oracle enrichment
Task<string?> ExecuteCreateEventDemoProcAsync(string messageId, string product, CancellationToken ct);   // SP with OUT param
Task InsertKddCaseCustomersAsync(string customerName, string clientRef, int transactionId, string caseId, CancellationToken ct);
Task UpdateKddCasesTitleAsync(string caseId, string transactionNumber, CancellationToken ct);
Task InsertTransFromRepoAsync(string customerName, string clientRef, int transactionId, string transactionNumber, CancellationToken ct);

// Existing KYC reads (already in the class, currently stubbed — fill bodies)
Task<string?> ReadCaseIdAsync(string fccmRequestId, CancellationToken ct);
Task<string?> ReadRiskCategoryKeyAsync(string fccmRequestId, CancellationToken ct);
```

13 new methods total. Each follows the same pattern:

```csharp
public async Task<TReturn> XxxAsync(<params>, CancellationToken ct = default)
{
    await using var con = new OracleConnection(_opt.FccmOracleConnection);
    await using var cmd = con.CreateCommand();
    cmd.BindByName = true;
    cmd.CommandText = "<SQL>";
    cmd.Parameters.Add(":p_x", OracleDbType.Varchar2).Value = (object?)x ?? DBNull.Value;
    // ... more params ...
    await con.OpenAsync(ct);
    await cmd.ExecuteNonQueryAsync(ct);    // or ExecuteScalarAsync / ExecuteReaderAsync
    return ...;
}
```

Optionally: extract a private `BuildCommand(con, sql, params)` helper
to remove repetition.

---

## 7. What Phase 2 needs to delete

When rewriting:

| File | Action |
|---|---|
| `Domain/Entities/FccmStaging/*.cs` (4 files) | **KEEP** as POCOs — drop the EF dependency by removing the configs |
| `Infrastructure/Persistence/Configurations/FccmStaging/*.cs` (4 files) | **DELETE** — no EF mapping needed |
| `Infrastructure/Persistence/FccmStagingDbContext.cs` | **DELETE** — replaced by per-op methods in FccmOracleReader |
| `IntegrationOptions.FccmStagingConnection` slot | **DELETE** — `FccmOracleConnection` covers everything |
| `appsettings.json` `Integration:FccmStagingConnection` | **DELETE** |
| `InfrastructureServiceRegistration.AddDbContext<FccmStagingDbContext>(...)` | **DELETE** |
| `FccmTradeCaseService` constructor + SubmitInternalAsync | **REWRITE** — use `FccmOracleReader` for all Oracle ops |

Two missing staging entities to add:

| File | Status |
|---|---|
| `Domain/Entities/FccmStaging/CustBankingStaging.cs` | **ADD** (legacy declares it, currently missing in our model) |
| `Domain/Entities/FccmStaging/RtCaseTracking.cs` | Probably not needed as a POCO — the table is internal to FccmOracleReader's tracking-row method |

---

## 8. Conclusion

**Option A is sufficient and safe.** Every legacy operation maps
cleanly to ADO.NET. No operation requires EF semantics. The stored
procedure call with OUT parameter is actually *easier* in raw ADO.NET
than in EF Core (which has clunky support for OUT params).

The rework is mechanical and ~2 hours of focused typing. The win is:

- One service file holds all Oracle access
- One NuGet instead of two
- ~25 MB lighter deploy
- No Oracle EF Core provider quirks to debug
- The legacy patterns ported faithfully (legacy is already 80% ADO.NET — only the staging INSERTs use EF)

**Migration risk to a future EF-based approach**: low. If we ever
need EF (e.g. analytics reads), we add `Oracle.EntityFrameworkCore`
side-by-side and keep `FccmOracleReader` for writes — no rework
required.
