// Slice 6 Step 3 — this entity was renamed to TransactionCustomerSnapshot
// for naming accuracy (the DB table TmX_Customer_Master is a per-transaction
// snapshot, NOT a global customer master of record — FCCM/BRAINS own that).
//
// The replacement entity lives at:
//   Domain/Entities/Customer/TransactionCustomerSnapshot.cs
//
// This file is intentionally empty so the old CustomerMaster type is no
// longer in scope. Safe to delete this file from source control once any
// branch / merge with references to it has been resolved.
namespace FSI.Trade.Compliance.Domain.Entities.Customer;
