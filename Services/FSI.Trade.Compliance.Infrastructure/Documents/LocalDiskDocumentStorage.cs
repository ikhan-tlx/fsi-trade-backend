using System.Security.Cryptography;
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
        CancellationToken ct = default)
    {
        if (content is null) throw new ArgumentNullException(nameof(content));
        if (string.IsNullOrWhiteSpace(originalFileName))
            throw new ArgumentException("Original file name is required.", nameof(originalFileName));

        var providerLkpId = await ResolveLocalDiskLookupAsync(ct);

        var now           = DateTime.UtcNow;
        var subfolder     = string.IsNullOrWhiteSpace(subfolderHint) ? "general" : Sanitise(subfolderHint);
        var relativeDir   = Path.Combine(subfolder, now.ToString("yyyy"), now.ToString("MM"));
        var absoluteDir   = Path.Combine(ResolveBasePath(), relativeDir);
        Directory.CreateDirectory(absoluteDir);

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
        catch
        {
            // Best-effort cleanup if a partial file got written.
            try { if (File.Exists(absolutePath)) File.Delete(absolutePath); } catch { /* swallow */ }
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

    private static string Sanitise(string subfolder)
    {
        var trimmed = subfolder.Trim().Trim('/', '\\');
        foreach (var ch in ForbiddenFileNameChars)
            trimmed = trimmed.Replace(ch.ToString(), "_");
        return string.IsNullOrWhiteSpace(trimmed) ? "general" : trimmed;
    }
}
