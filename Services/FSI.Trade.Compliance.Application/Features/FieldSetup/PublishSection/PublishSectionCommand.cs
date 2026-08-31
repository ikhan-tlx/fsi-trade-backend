using MediatR;

namespace FSI.Trade.Compliance.Application.Features.FieldSetup.PublishSection;

/// <summary>
/// Field Configuration — Builder (Option A) "Review &amp; publish" batch.
///
/// Applies a whole section's pending edits in ONE transaction: label /
/// placeholder / visible (Is_Active) / required (Is_Mandatory) / type
/// (Field_Type_Lkp, guarded) per field, section moves (Parent_Tenant_Field_
/// Setup_Id), and — most importantly — a clean re-write of
/// <c>Field_Sequence</c> derived from the editor's display order.
///
/// <para><b>Sequencing.</b> The FE sends each item's <see cref="PublishSectionItem.order"/>
/// — its 0-based position in the flattened display order (each header followed
/// by its fields, each table by its columns, blocks in order). The handler
/// sorts by <c>order</c> and re-emits <c>Field_Sequence</c> as 10, 20, 30, …
/// (gaps of 10). A single monotonic global sequence keeps every sibling group
/// correctly ordered (children are grouped by parent and sorted by sequence;
/// blocks are sorted among themselves) while letting the admin think only in
/// relative 1·2·3 per group.</para>
///
/// <para><b>Guardrails (re-validated server-side — the FE matrix is advisory).</b>
/// A <see cref="PublishSectionItem.fieldTypeLkp"/> change is accepted only when
/// it is in <c>FieldTypeSafeMatrix</c> (Text ↔ Long text today), the field is
/// not lookup-bound, and it is not formula-managed — otherwise 409
/// <c>unsafe_type_change</c>. An <see cref="PublishSectionItem.isMandatory"/>
/// change against a formula-driven rule returns 409
/// <c>field_has_conditional_rule</c> (same rule as the single-field PUT). Every
/// item must belong to <see cref="productId"/> (cross-product edits rejected).</para>
/// </summary>
public class PublishSectionCommand : IRequest<PublishSectionResult>
{
    /// <summary>Product whose form-schema cache is bumped after the batch.</summary>
    public int productId { get; set; }

    /// <summary>Tab (sub-type) the batch is scoped to. Required when the batch
    /// INSERTS new rows (they need a Tab_Id); optional for pure edits.</summary>
    public int? tabId { get; set; }

    /// <summary>The full ordered set of rows in the edited section.</summary>
    public List<PublishSectionItem> items { get; set; } = new();
}

public class PublishSectionItem
{
    /// <summary>Target row (TmX_Tenant_Field_Setup PK).</summary>
    public int tenantFieldSetupId { get; set; }

    /// <summary>0-based position in the flattened display order. Drives the
    /// re-written Field_Sequence.</summary>
    public int order { get; set; }

    /// <summary>New parent (cross-section move). Omit / null to leave the
    /// existing parent unchanged.</summary>
    public int? parentTenantFieldSetupId { get; set; }

    /// <summary>Set true to apply <see cref="parentTenantFieldSetupId"/> (incl.
    /// setting it to null/root). Without this flag the parent is left as-is, so
    /// a normal reorder never disturbs parentage.</summary>
    public bool moveParent { get; set; }

    public string? fieldLabel  { get; set; }
    public string? placeholder { get; set; }
    public bool?   isActive    { get; set; }
    public bool?   isMandatory { get; set; }

    /// <summary>"Auto-resolved amount field" radio (Is_Amount_Field). Null = leave
    /// unchanged. At most one item per sub-product should be true; the handler enforces
    /// mutual exclusivity per (Product, Tab) and rejects non-numeric fields (409).</summary>
    public bool?   isAmountField { get; set; }

    /// <summary>Requested new Field_Type_Lkp. Null = leave unchanged. Validated
    /// against FieldTypeSafeMatrix. For an INSERT (<see cref="isNew"/>) this is the
    /// created row's type and is REQUIRED.</summary>
    public int?    fieldTypeLkp { get; set; }

    // ---- INSERT support (Add field / Add section) --------------------------
    // An item with isNew=true creates a brand-new TmX_Tenant_Field_Setup row.
    // Its tenantFieldSetupId is ignored; it is keyed within the batch by tempId
    // (a client-generated id) so children can point at a not-yet-inserted parent.

    /// <summary>True ⇒ this item is a new row to INSERT (not an edit).</summary>
    public bool    isNew { get; set; }

    /// <summary>Client-generated id for a new row, used only to link children to a
    /// new parent within the same batch (see <see cref="parentTempId"/>).</summary>
    public int?    tempId { get; set; }

    /// <summary>Parent is another NEW row in this batch, referenced by its tempId
    /// (e.g. a field under a brand-new heading). Resolved to a real id after insert.</summary>
    public int?    parentTempId { get; set; }

    /// <summary>Explicit Field_Name for a new row. Optional — the server generates
    /// a unique, prefixed name from the label when blank, and always re-validates.</summary>
    public string? fieldName { get; set; }

    /// <summary>Field_Lookup_Type for a new dropdown (25) — a TmX_Lookup.Lookup_Type
    /// string. Required when the new row is a dropdown.</summary>
    public string? fieldLookupType { get; set; }

    /// <summary>Optional Field_Table_Name override for a new row. When blank the
    /// server inherits it from the parent table / section siblings.</summary>
    public string? fieldTableName { get; set; }
}

/// <summary>Summary so the FE can confirm what landed.</summary>
public class PublishSectionResult
{
    public int productId { get; set; }
    public int updated   { get; set; }  // rows touched
    public int reordered { get; set; }  // rows whose Field_Sequence changed
    public int retyped   { get; set; }  // rows whose Field_Type_Lkp changed
    public int moved     { get; set; }  // rows whose parent changed
    public int inserted  { get; set; }  // brand-new rows created
}
