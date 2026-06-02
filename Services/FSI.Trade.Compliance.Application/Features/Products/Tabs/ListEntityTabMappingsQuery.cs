// Slice 10 §FE-API cleanup (2026-05-25) — query type removed along with
// its endpoint (GET /api/v1/Product/Tabs/Mappings). Per-product callers
// now use /Product/{id}/Tabs which carries the selectorTabId signal.
//
// File kept as a tombstone because the sandbox file tools can't delete
// on the Windows mount. Run this in PowerShell to finish the cleanup:
//
//   Remove-Item "D:\ICBC - Latest\FSI.Trade.Compliance\Services\FSI.Trade.Compliance.Application\Features\Products\Tabs\ListEntityTabMappingsQuery.cs"
//   Remove-Item "D:\ICBC - Latest\FSI.Trade.Compliance\Services\FSI.Trade.Compliance.Application\Features\Products\Tabs\ListEntityTabMappingsQueryHandler.cs"
//
// Empty file content below — compiles cleanly, no symbols defined.
