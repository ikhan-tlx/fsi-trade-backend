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
    /// against FieldTypeSafeMatrix.</summary>
    public int?    fieldTypeLkp { get; set; }
}

/// <summary>Summary so the FE can confirm what landed.</summary>
public class PublishSectionResult
{
    public int productId { get; set; }
    public int updated   { get; set; }  // rows touched
    public int reordered { get; set; }  // rows whose Field_Sequence changed
    public int retyped   { get; set; }  // rows whose Field_Type_Lkp changed
    public int moved     { get; set; }  // rows whose parent changed
}
