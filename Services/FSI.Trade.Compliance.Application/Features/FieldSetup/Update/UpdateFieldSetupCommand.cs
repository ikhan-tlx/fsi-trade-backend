using MediatR;

namespace FSI.Trade.Compliance.Application.Features.FieldSetup.Update;

/// <summary>
/// Slice 12 §A — mutates a single <c>TmX_Tenant_Field_Setup</c> row
/// from the Field Configuration admin page.
///
/// <para>
/// Only four properties are editable through this endpoint:
/// <see cref="fieldLabel"/>, <see cref="placeholder"/>,
/// <see cref="isActive"/> (the "Visible on form" toggle), and
/// <see cref="isMandatory"/> (the "Required" toggle). All four are
/// nullable — null means "don't change this property". Pass only the
/// keys the admin actually toggled so the round-trip is minimal.
/// </para>
///
/// <para>
/// <b>isMandatory semantics — important.</b> The underlying column
/// <c>Is_Mandatory</c> is a string formula evaluated client-side
/// (e.g. <c>"tf274526 == true &amp;&amp; tf274548 != ''"</c>). The admin
/// toggle wants a binary on/off. The handler detects the existing
/// value: null / empty / literal <c>"true"</c> / literal <c>"false"</c>
/// are safe to overwrite; anything else is treated as a conditional
/// formula and the request is rejected with 409
/// <c>field_has_conditional_rule</c>. The FE renders a "managed by
/// platform — formula-driven" hint in that case.
/// </para>
///
/// <para>
/// Field type / parent linkage / locale ID / formula / visibility
/// formulas / allowed-state CSV — all left untouched. Adding or
/// removing fields, changing types, or editing formulas is
/// platform-team work via direct DB or a separate (future) endpoint.
/// </para>
/// </summary>
public class UpdateFieldSetupCommand : IRequest<UpdateFieldSetupResult>
{
    /// <summary>Bound from the route. Body shape carries no id.</summary>
    public int      tenantFieldSetupId { get; set; }

    /// <summary>New label (Field_Label column). Null = don't change.</summary>
    public string?  fieldLabel         { get; set; }

    /// <summary>New placeholder hint (Placeholder column). Null = don't change. Empty string clears the value.</summary>
    public string?  placeholder        { get; set; }

    /// <summary>"Visible on form" toggle (Is_Active column). Null = don't change.</summary>
    public bool?    isActive           { get; set; }

    /// <summary>"Required" toggle (Is_Mandatory column). Null = don't change. Conditional formulas reject with 409.</summary>
    public bool?    isMandatory        { get; set; }

    /// <summary>"Auto-resolved amount field" radio (Is_Amount_Field). Null = don't change.
    /// Setting true designates THIS field as the sub-product's amount field and clears the
    /// flag from every other field in the same (Product, Tab) — mutual exclusivity. Only
    /// numeric/amount-typed fields may be set; otherwise rejected 409.</summary>
    public bool?    isAmountField      { get; set; }
}

/// <summary>Echoes the field's post-update state so the FE doesn't need a re-fetch.</summary>
public class UpdateFieldSetupResult
{
    public int      tenantFieldSetupId { get; set; }
    public int      productId          { get; set; }
    public int?     tabId              { get; set; }
    public string?  fieldName          { get; set; }
    public string?  fieldLabel         { get; set; }
    public string?  placeholder        { get; set; }
    public bool     isActive           { get; set; }
    public string?  isMandatory        { get; set; }
    public bool     isAmountField      { get; set; }
}
