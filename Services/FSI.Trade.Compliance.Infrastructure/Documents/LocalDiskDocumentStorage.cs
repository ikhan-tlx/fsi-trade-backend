using System.IO;
using System.Security.Cryptography;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Documents;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Documents;

/// <summary>
/// Filesystem implementation of <see cref="IDocumentStorage"/>. Files
/// land under <c>{BasePath}/{subfolder}/{yyyy}/{MM}/{guid}.{ext}</c>.
/// SHA-256 is computed in-stream during write — one disk pass, no
/// double-read.
///
/// Base-path resolution:
///   • Absolute path in config → used as-is.
///   • Relative path (default <c>"ICBC_Data"</c>) → resolved against
///     <see cref="IHostEnvironment.ContentRootPath"/>.
///   • Empty string → falls back to <c>{ContentRoot}/ICBC_Data</c>.
///
/// The STORAGE_PROVIDER lookup row for <c>LOCAL_DISK</c> is resolved
/// per-call. The lookup is indexed and the row count is tiny, so this
/// is essentially free and avoids stale-cache pitfalls.
/// </summary>
internal class LocalDiskDocumentStorage : IDocumentStorage
{
    // Characters Windows forbids in filenames. Keeps the sanitiser
    // OS-agnostic (Linux is more permissive but stripping these is fine).
    private static readonly char[] ForbiddenFileNameChars =
        { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

    private readonly IApplicationDbContext _db;
    private readonly DocumentOptions       _options;
    private readonly IHostEnvironment      _env;
    private readonly ILogger<LocalDiskDocumentStorage> _logger;

    public LocalDiskDocumentStorage(
        IApplicationDbContext db,
        IOptions<DocumentOptions> options,
        IHostEnvironment env,
        ILogger<LocalDiskDocumentStorage> logger)
    {
        _db      = db;
        _options = options.Value;
        _env     = env;
        _logger  = logger;
    }

    public async Task<Document> SaveAsync(
        Stream content,
        string originalFileName,
        string? mimeType,
        string uploadedBy,
        int tenantId,
        string subfolderHint,
        bool appendDatePartition = true,
        CancellationToken ct = default)
    {
        if (content is null) throw new ArgumentNullException(nameof(content));
        if (string.IsNullOrWhiteSpace(originalFileName))
            throw new ArgumentException("Original file name is required.", nameof(originalFileName));

        var providerLkpId = await ResolveLocalDiskLookupAsync(ct);

        var now           = DateTime.UtcNow;
        var subfolder     = Sanitise(subfolderHint);   // empty → "general"; multi-segment + traversal-safe
        // Transaction-scoped callers supply an already-structured path (e.g.
        // Transaction/<number>[/FlagsEvidence]) and pass appendDatePartition=false so it
        // is used verbatim; other callers keep the /yyyy/MM/ month partition.
        var relativeDir   = appendDatePartition
            ? Path.Combine(subfolder, now.ToString("yyyy"), now.ToString("MM"))
            : subfolder;
        var absoluteDir   = Path.Combine(ResolveBasePath(), relativeDir);
        try
        {
            Directory.CreateDirectory(absoluteDir);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // A missing/read-only storage folder or a process identity without write
            // permission is a SERVER-side configuration fault — surface it as such
            // (500, logged) rather than letting the raw UnauthorizedAccessException be
            // mapped to HTTP 401 (which would pop the login modal and hide the cause).
            _logger.LogError(ex,
                "Document storage directory is not writable: '{Dir}'. Check Documents.BasePath and the app-pool identity's Modify permission.",
                absoluteDir);
            throw new DocumentStorageException(
                $"The document storage location is not writable ('{absoluteDir}').", ex);
        }

        var safeOriginalName = SanitiseFileName(originalFileName);
        var extension        = Path.GetExtension(safeOriginalName);
        var storedFileName   = $"{Guid.NewGuid():N}{extension}";
        var absolutePath     = Path.Combine(absoluteDir, storedFileName);
        var relativePath     = Path.Combine(relativeDir,  storedFileName);

        long fileSize;
        string hashHex;
        try
        {
            (fileSize, hashHex) = await WriteAndHashAsync(content, absolutePath, ct);
        }
        catch (Exception ex)
        {
            // Best-effort cleanup if a partial file got written.
            try { if (File.Exists(absolutePath)) File.Delete(absolutePath); } catch { /* swallow */ }

            // Translate filesystem permission/IO failures to a domain error so the API
            // maps them to 500 (logged) instead of the generic
            // UnauthorizedAccessException→401 (which surfaces as a spurious login modal).
            if (ex is UnauthorizedAccessException or IOException)
            {
                _logger.LogError(ex,
                    "Failed to write document to '{Path}'. Check Documents.BasePath and the app-pool identity's Modify permission.",
                    absolutePath);
                throw new DocumentStorageException(
                    $"The document could not be written to storage ('{absolutePath}').", ex);
            }
            throw;
        }

        _logger.LogInformation(
            "Stored document '{Original}' as '{Stored}' under '{Relative}' ({Size} bytes)",
            safeOriginalName, storedFileName, relativePath, fileSize);

        return new Document
        {
            OriginalFileName      = safeOriginalName,
            StoredFileName        = storedFileName,
            MimeType              = mimeType,
            FileSizeBytes         = fileSize,
            Sha256Hash            = hashHex,
            StorageProviderLkpId  = providerLkpId,
            StorageRelativePath   = relativePath.Replace('\\', '/'),
            TenantId              = tenantId,
            ActiveFlag            = true,
            UploadedBy            = uploadedBy,
            UploadedDate          = now,
            CreatedBy             = uploadedBy,
            CreatedDate           = now,
        };
    }

    public Task<Stream> OpenReadAsync(Document document, CancellationToken ct = default)
    {
        var absolutePath = Path.Combine(ResolveBasePath(), document.StorageRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolutePath))
            throw new FileNotFoundException("Document file not found on local disk.", absolutePath);

        Stream stream = new FileStream(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81_920,
            useAsync: true);
        return Task.FromResult(stream);
    }

    public Task<bool> DeleteAsync(Document document, CancellationToken ct = default)
    {
        var absolutePath = Path.Combine(ResolveBasePath(), document.StorageRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolutePath))
        {
            _logger.LogInformation(
                "DeleteAsync: document '{Stored}' already absent from disk at '{Path}'",
                document.StoredFileName, absolutePath);
            return Task.FromResult(false);
        }

        File.Delete(absolutePath);
        _logger.LogInformation("Deleted document file at '{Path}'", absolutePath);
        return Task.FromResult(true);
    }

    // ---- helpers --------------------------------------------------------

    private string ResolveBasePath()
    {
        var configured = _options.BasePath;
        if (string.IsNullOrWhiteSpace(configured)) configured = "ICBC_Data";

        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(_env.ContentRootPath, configured);
    }

    /// <summary>
    /// Resolves the TmX_Lookup row for the LOCAL_DISK storage provider.
    /// Throws if the seed migration (2026_05_012) hasn't run.
    /// </summary>
    private async Task<int> ResolveLocalDiskLookupAsync(CancellationToken ct)
    {
        var id = await _db.Lookups
            .AsNoTracking()
            .Where(l => l.LookupType == "STORAGE_PROVIDER"
                     && l.HiddenValue == "LOCAL_DISK")
            .Select(l => (int?)l.Id)
            .FirstOrDefaultAsync(ct);

        if (id is null)
            throw new InvalidOperationException(
                "STORAGE_PROVIDER lookup row for LOCAL_DISK is missing. " +
                "Run migration 2026_05_012_SeedFlagLookups.sql.");

        return id.Value;
    }

    private static async Task<(long Size, string Sha256Hex)> WriteAndHashAsync(
        Stream source, string absolutePath, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        await using var dest = new FileStream(
            absolutePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 81_920, useAsync: true);

        var buffer  = new byte[81_920];
        long total  = 0;
        int  read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, read), ct);
            sha.TransformBlock(buffer, 0, read, null, 0);
            total += read;
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        return (total, Convert.ToHexString(sha.Hash!));
    }

    private static string SanitiseFileName(string name)
    {
        var trimmed = name.Trim();
        foreach (var ch in ForbiddenFileNameChars)
            trimmed = trimmed.Replace(ch.ToString(), "_");
        // Path.GetFileName drops any directory component the user tries to
        // sneak in (e.g. "..\\..\\etc\\passwd").
        return Path.GetFileName(trimmed);
    }

    /// <summary>
    /// Sanitises a subfolder hint into a SAFE, possibly-NESTED relative path
    /// (e.g. "transaction/detail" → transaction/detail/yyyy/MM/…). Splits on
    /// both separators, cleans each segment of forbidden chars, and drops
    /// empty / "."/".." segments so a hint can't traverse out of the base path.
    /// </summary>
    private static string Sanitise(string subfolder)
    {
        if (string.IsNullOrWhiteSpace(subfolder)) return "general";

        var clean = new List<string>();
        foreach (var rawSegment in subfolder.Replace('\\', '/').Split('/'))
        {
            var seg = rawSegment.Trim();
            if (seg.Length == 0 || seg == "." || seg == "..") continue;
            foreach (var ch in ForbiddenFileNameChars)
                seg = seg.Replace(ch.ToString(), "_");
            seg = seg.Trim();
            if (seg.Length > 0) clean.Add(seg);
        }

        return clean.Count == 0 ? "general" : string.Join("/", clean);
    }
}
