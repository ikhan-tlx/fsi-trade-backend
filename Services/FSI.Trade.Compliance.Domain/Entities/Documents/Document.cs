namespace FSI.Trade.Compliance.Domain.Entities.Documents;

/// <summary>
/// Generic file-attachment store. Maps to <c>dbo.TmX_Document</c>
/// (Slice 8 Step 1).
///
/// Storage-provider-agnostic via <see cref="StorageProviderLkpId"/> —
/// the same row can describe a file on local disk (<c>ICBC_Data</c>
/// folder), in Azure Blob Storage, or in S3. The actual byte-stream
/// resolution is the <c>IDocumentStorage</c> service's job.
///
/// Linkage is one-way: referring tables (e.g.
/// <c>TmX_Transaction_Flag.Evidence_Document_ID</c>) hold the FK to
/// this entity. We deliberately don't carry a polymorphic
/// "source_module / source_reference_id" pair on the document itself —
/// that pattern hurts query plans and breaks FK enforcement. If
/// multiple-references-per-document is needed later, a join table is
/// cheaper than rebuilding.
/// </summary>
public class Document
{
    public int       DocumentId            { get; set; }

    /// <summary>Filename as the user uploaded it. Used for Content-Disposition + audit.</summary>
    public string    OriginalFileName      { get; set; } = "";

    /// <summary>GUID-based filename on disk (or blob key). Format: <c>&lt;guid&gt;.&lt;ext&gt;</c>.</summary>
    public string    StoredFileName        { get; set; } = "";

    public string?   MimeType              { get; set; }
    public long?     FileSizeBytes         { get; set; }

    /// <summary>SHA-256 of file contents. Integrity check on retrieval; dedupe key for future cost optimisation.</summary>
    public string?   Sha256Hash            { get; set; }

    /// <summary>FK to TmX_Lookup row with Lookup_Type='STORAGE_PROVIDER' (LOCAL_DISK / AZURE_BLOB / S3).</summary>
    public int       StorageProviderLkpId  { get; set; }

    /// <summary>Relative path within the provider. For LOCAL_DISK, appended to <c>Documents:StoragePath</c> from appsettings.</summary>
    public string    StorageRelativePath   { get; set; } = "";

    public int       TenantId              { get; set; } = 1;
    public bool      ActiveFlag            { get; set; } = true;

    public string    UploadedBy            { get; set; } = "";
    public DateTime  UploadedDate          { get; set; }

    public string    CreatedBy             { get; set; } = "";
    public DateTime  CreatedDate           { get; set; }
    public string?   LastUpdatedBy         { get; set; }
    public DateTime? LastUpdatedDate       { get; set; }
}
