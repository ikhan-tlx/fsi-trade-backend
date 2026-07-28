using System.Text.Json;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Features.AutoFlags;
using FSI.Trade.Compliance.Application.Features.AutoFlags.Apply;
using FSI.Trade.Compliance.Application.Features.CompanyBranches.Create;
using FSI.Trade.Compliance.Application.Features.CompanyBranches.Update;
using FSI.Trade.Compliance.Application.Features.FieldSetup.PublishSection;
using FSI.Trade.Compliance.Application.Features.Flags.Admin.Apply;
using FSI.Trade.Compliance.Application.Features.Flags.Admin.Delete;
using FSI.Trade.Compliance.Application.Features.ReferenceLists;
using FSI.Trade.Compliance.Application.Features.Roles.Apply;
using FSI.Trade.Compliance.Application.Features.Roles.SetActive;
using FSI.Trade.Compliance.Application.Features.Users.Create;
using FSI.Trade.Compliance.Application.Features.Users.SetActive;
using FSI.Trade.Compliance.Application.Features.Users.Update;

namespace FSI.Trade.Compliance.Application.Features.Requests.Common;

/// <summary>
/// Maker/checker replay registry — the single place that maps a stored request
/// (<c>Request_Type_Code</c> + <c>Entity_Status_Code</c>) back to the exact
/// MediatR command the maker would have run. Approval re-dispatches that command
/// through its real write handler (all its validation + persistence) instead of
/// duplicating apply logic per entity.
///
/// It also declares which privilege a maker needs to SUBMIT a given change, and
/// whether a (type, action) pair is gated by maker/checker at all.
///
/// Codes mirror the FE <c>requests-constants.ts</c> map:
///   Types   0001 User · 0002 Lookup · 0003 Role · 0004 Branch · 0005 Auto Flag
///   Actions 0001 Create · 0002 Update · 0003 Delete · 0004 Deactivate
/// Phase 1 wires User (Create / Update / Deactivate); later phases extend the
/// two switches below for Role, Branch, Lookup and Auto Flag.
/// </summary>
public interface IApprovalReplayRegistry
{
    bool   IsSupported(string? requestTypeCode, string? entityStatusCode);
    string RequiredSubmitPrivilege(string? requestTypeCode, string? entityStatusCode);
    object BuildReplayCommand(string? requestTypeCode, string? entityStatusCode, string? entityId, string? entityJson);
}

public sealed class ApprovalReplayRegistry : IApprovalReplayRegistry
{
    // Request type codes.
    public const string TypeUser     = "0001";
    public const string TypeLookup   = "0002";
    public const string TypeRole     = "0003";
    public const string TypeBranch   = "0004";
    public const string TypeAutoFlag = "0005";
    public const string TypeManualFlag = "0006";
    public const string TypeFieldConfig = "0007";
    // Reference Lists — one request type per sub-entity so the (type, action)
    // switch can pick the exact command (types + values + bulk share action codes).
    public const string TypeReferenceType  = "0008";
    public const string TypeReferenceValue = "0009";
    public const string TypeReferenceBulk  = "0010";

    // Entity action codes.
    public const string ActionCreate     = "0001";
    public const string ActionUpdate     = "0002";
    public const string ActionDelete     = "0003";
    public const string ActionDeactivate = "0004";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public bool IsSupported(string? type, string? action)
    {
        type = type?.Trim(); action = action?.Trim();
        return (type, action) switch
        {
            (TypeUser,   ActionCreate)     => true,
            (TypeUser,   ActionUpdate)     => true,
            (TypeUser,   ActionDeactivate) => true,
            (TypeRole,   ActionCreate)     => true,
            (TypeRole,   ActionUpdate)     => true,
            (TypeRole,   ActionDeactivate) => true,
            (TypeBranch, ActionCreate)     => true,
            (TypeBranch, ActionUpdate)     => true,
            // Auto Flag EDIT of an already-Active flag: parked via the generic Submit
            // endpoint and replayed (create-or-update + activate) on approve, so the
            // live flag is never touched until approval and a reject is a no-op. (The
            // Draft/new-flag "activate" path uses AutoFlag/SubmitForApproval, action 0001.)
            (TypeAutoFlag, ActionUpdate)   => true,
            (TypeManualFlag, ActionCreate) => true,
            (TypeManualFlag, ActionUpdate) => true,
            (TypeManualFlag, ActionDelete) => true,   // retire
            // Field Configuration: a section "Review & publish" batch — parked and
            // replayed (PublishSectionCommand) on approve so the live form schema is
            // never changed until a checker approves; reject is a no-op.
            (TypeFieldConfig, ActionUpdate) => true,
            // Reference Lists — every write is gated (types + values + bulk). The
            // live reference tables are never touched until a checker approves;
            // reject is a no-op (the maker's command is only replayed on approve).
            (TypeReferenceType,  ActionCreate) => true,
            (TypeReferenceType,  ActionUpdate) => true,
            (TypeReferenceType,  ActionDelete) => true,
            (TypeReferenceValue, ActionCreate) => true,
            (TypeReferenceValue, ActionUpdate) => true,
            (TypeReferenceValue, ActionDelete) => true,
            (TypeReferenceBulk,  ActionUpdate) => true,   // bulk value import into a type
            _ => false,
        };
    }

    public string RequiredSubmitPrivilege(string? type, string? action)
    {
        type = type?.Trim(); action = action?.Trim();
        return (type, action) switch
        {
            (TypeUser,   ActionCreate)     => "Users.Create",
            (TypeUser,   ActionUpdate)     => "Users.Update",
            (TypeUser,   ActionDeactivate) => "Users.Activate",
            (TypeRole,   ActionCreate)     => "Roles.Manage",
            (TypeRole,   ActionUpdate)     => "Roles.Manage",
            (TypeRole,   ActionDeactivate) => "Roles.Manage",
            (TypeBranch, ActionCreate)     => "Masterdata.View",
            (TypeBranch, ActionUpdate)     => "Masterdata.View",
            (TypeAutoFlag, ActionUpdate)   => "AutoFlags.Author",
            (TypeManualFlag, ActionCreate) => "Flags.Manage",
            (TypeManualFlag, ActionUpdate) => "Flags.Manage",
            (TypeManualFlag, ActionDelete) => "Flags.Manage",
            (TypeFieldConfig, ActionUpdate) => "FieldSetup.Manage",
            (TypeReferenceType,  ActionCreate) => "ReferenceLists.Manage",
            (TypeReferenceType,  ActionUpdate) => "ReferenceLists.Manage",
            (TypeReferenceType,  ActionDelete) => "ReferenceLists.Manage",
            (TypeReferenceValue, ActionCreate) => "ReferenceLists.Manage",
            (TypeReferenceValue, ActionUpdate) => "ReferenceLists.Manage",
            (TypeReferenceValue, ActionDelete) => "ReferenceLists.Manage",
            (TypeReferenceBulk,  ActionUpdate) => "ReferenceLists.Manage",
            _ => throw new ConflictException("request_type_unsupported",
                    $"Maker/checker is not configured for request type '{type}' action '{action}'."),
        };
    }

    public object BuildReplayCommand(string? type, string? action, string? entityId, string? entityJson)
    {
        type = type?.Trim(); action = action?.Trim();
        switch ((type, action))
        {
            case (TypeUser, ActionCreate):
                return Deserialize<CreateUserCommand>(entityJson);

            case (TypeUser, ActionUpdate):
                var upd = Deserialize<UpdateUserCommand>(entityJson);
                if (!string.IsNullOrWhiteSpace(entityId)) upd.userId = entityId!;
                return upd;

            case (TypeUser, ActionDeactivate):
                if (string.IsNullOrWhiteSpace(entityId))
                    throw new ConflictException("entity_id_required", "A user id is required to deactivate.");
                return new SetUserActiveCommand(entityId!, false);

            // ---- Role (record + privileges bundled via ApplyRoleChangeCommand) ----
            case (TypeRole, ActionCreate):
                return Deserialize<ApplyRoleChangeCommand>(entityJson);

            case (TypeRole, ActionUpdate):
                var role = Deserialize<ApplyRoleChangeCommand>(entityJson);
                if (int.TryParse(entityId, out var rid)) role.roleId = rid;
                return role;

            case (TypeRole, ActionDeactivate):
                if (!int.TryParse(entityId, out var deRid))
                    throw new ConflictException("entity_id_required", "A role id is required to deactivate.");
                return new SetRoleActiveCommand(deRid, false);

            // ---- Company Branch ----
            case (TypeBranch, ActionCreate):
                return Deserialize<CreateCompanyBranchCommand>(entityJson);

            case (TypeBranch, ActionUpdate):
                var branch = Deserialize<UpdateCompanyBranchCommand>(entityJson);
                if (int.TryParse(entityId, out var bid)) branch.CompanyBranchId = bid;
                return branch;

            // ---- Auto Flag ----
            // Action 0001 (Create): a Draft/new flag was built on the row and submitted
            // for approval (AutoFlag/SubmitForApproval). Approving just activates it — the
            // config already lives on the flag row. Reject returns it to Draft.
            case (TypeAutoFlag, ActionCreate):
                if (!int.TryParse(entityId, out var activateFlagId))
                    throw new ConflictException("entity_id_required", "An auto-flag id is required.");
                return new ApproveAutoFlagCommand(activateFlagId);

            // Action 0002 (Update): an EDIT of an already-Active flag, parked via the
            // generic Submit endpoint. The live flag was NOT touched; approving replays the
            // full proposed config (create-or-update + activate). Reject is a no-op.
            case (TypeAutoFlag, ActionUpdate):
                var afEdit = Deserialize<ApplyAutoFlagChangeCommand>(entityJson);
                if (int.TryParse(entityId, out var afEditId)) afEdit.flagId = afEditId;
                return afEdit;

            // ---- Manual Red Flag (record + active state bundled via ApplyManualFlagChangeCommand) ----
            case (TypeManualFlag, ActionCreate):
                return Deserialize<ApplyManualFlagChangeCommand>(entityJson);

            case (TypeManualFlag, ActionUpdate):
                var mflag = Deserialize<ApplyManualFlagChangeCommand>(entityJson);
                if (int.TryParse(entityId, out var mfid)) mflag.flagId = mfid;
                return mflag;

            case (TypeManualFlag, ActionDelete):
                if (!int.TryParse(entityId, out var delMfid))
                    throw new ConflictException("entity_id_required", "A flag id is required to retire.");
                return new DeleteFlagCommand(delMfid);

            // ---- Field Configuration: replay the whole section "publish" batch ----
            // Extra display-only fields the FE parks for the checker (e.g.
            // changesSummary) are ignored by System.Text.Json when binding to
            // PublishSectionCommand, so the replay body stays canonical.
            case (TypeFieldConfig, ActionUpdate):
                return Deserialize<PublishSectionCommand>(entityJson);

            // ---- Reference Lists ----
            // TYPE create/update (details + full membership set) and delete.
            case (TypeReferenceType, ActionCreate):
                return Deserialize<SaveReferenceTypeCommand>(entityJson);
            case (TypeReferenceType, ActionUpdate):
                var rt = Deserialize<SaveReferenceTypeCommand>(entityJson);
                if (long.TryParse(entityId, out var rtId)) rt.id = rtId;
                return rt;
            case (TypeReferenceType, ActionDelete):
                if (!long.TryParse(entityId, out var delRtId))
                    throw new ConflictException("entity_id_required", "A reference type id is required to delete.");
                return new DeleteReferenceTypeCommand(delRtId);

            // VALUE create/update (catalogue value + its type memberships) and delete.
            case (TypeReferenceValue, ActionCreate):
                return Deserialize<SaveReferenceValueCommand>(entityJson);
            case (TypeReferenceValue, ActionUpdate):
                var rv = Deserialize<SaveReferenceValueCommand>(entityJson);
                if (long.TryParse(entityId, out var rvId)) rv.id = rvId;
                return rv;
            case (TypeReferenceValue, ActionDelete):
                if (!long.TryParse(entityId, out var delRvId))
                    throw new ConflictException("entity_id_required", "A reference value id is required to delete.");
                return new DeleteReferenceValueCommand(delRvId);

            // BULK value import into a type — the maker parked only the VALID rows
            // (invalid rows were reported at upload time). Replay applies them.
            case (TypeReferenceBulk, ActionUpdate):
                var bulk = Deserialize<BulkUpsertReferenceValuesCommand>(entityJson);
                if (long.TryParse(entityId, out var bulkTypeId)) bulk.typeId = bulkTypeId;
                return bulk;

            default:
                throw new ConflictException("request_type_unsupported",
                    $"Maker/checker replay is not configured for request type '{type}' action '{action}'.");
        }
    }

    private static T Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ConflictException("request_payload_missing", "The request has no payload to apply.");
        T? cmd;
        try { cmd = JsonSerializer.Deserialize<T>(json!, Json); }
        catch (JsonException ex)
        {
            throw new ConflictException("request_payload_invalid", $"The request payload could not be parsed: {ex.Message}");
        }
        if (cmd is null)
            throw new ConflictException("request_payload_invalid", "The request payload could not be parsed.");
        return cmd;
    }
}
