namespace FSI.Trade.Compliance.Application.Common.Options;

/// <summary>
/// Slice 8 — document-storage tuning. Bound from the <c>Documents</c>
/// section in appsettings.json.
///
/// The flag stack's evidence attachments are written under
/// <see cref="BasePath"/> via <c>LocalDiskDocumentStorage</c>. Layout:
/// <c>{BasePath}/{Subfolder}/{yyyy}/{MM}/{guid}.{ext}</c>.
///
/// In production this points at the deployed server's <c>ICBC_Data</c>
/// folder. In dev override via appsettings.Development.json to a
/// project-local path so test runs don't litter the prod folder.
/// </summary>
public class DocumentOptions
{
    public const string SectionName = "Documents";

    /// <summary>
    /// Base directory for the local-disk storage provider. Can be a
    /// relative path (resolved against the host's <c>ContentRootPath</c>)
    /// or an absolute path. Empty string falls back to
    /// <c>{ContentRootPath}/ICBC_Data</c>.
    /// </summary>
    public string BasePath { get; set; } = "ICBC_Data";
}
