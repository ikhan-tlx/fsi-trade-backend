// detailSections was dropped 2026-06-04 — a server-built sectioned readout is
// redundant: a sub-product tab's field values are spread across ~17 tables'
// UDF_Data columns (partitioned by Field_Table_Name), and the FE already
// renders the read-only detail from the form schema + per-table UDF.
// This file is intentionally empty and can be deleted from the project.
