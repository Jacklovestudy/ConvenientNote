using System.IO.Compression;
using System.IO;
using System.Text.Json;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Workspaces;

namespace ConvenientNote.Services;

public sealed class WorkspaceBackupService
{
    private const string BackupFormat = "convenient-note-backup";
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WorkspaceApplicationService _workspaceApplicationService;
    private readonly NoteMediaService _noteMediaService;

    public WorkspaceBackupService(
        WorkspaceApplicationService workspaceApplicationService,
        NoteMediaService noteMediaService)
    {
        _workspaceApplicationService = workspaceApplicationService;
        _noteMediaService = noteMediaService;
    }

    public async Task<WorkspaceBackupExportResult> ExportAsync(
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var destinationFullPath = Path.GetFullPath(destinationPath);
        var destinationDirectory = Path.GetDirectoryName(destinationFullPath)
            ?? throw new InvalidOperationException("Backup destination must have a directory.");
        Directory.CreateDirectory(destinationDirectory);

        var temporaryPath = $"{destinationFullPath}.tmp-{Guid.NewGuid():N}";
        try
        {
            var snapshot = await _workspaceApplicationService
                .GetOrCreateDefaultWorkspaceAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            using (var packageStream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 81920,
                       useAsync: true))
            using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                var manifest = new WorkspaceBackupManifest(
                    BackupFormat,
                    SchemaVersion,
                    "1.0.0",
                    DateTimeOffset.UtcNow);
                var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                await using (var manifestStream = manifestEntry.Open())
                {
                    await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken);
                }

                var workspaceEntry = archive.CreateEntry("workspace.json", CompressionLevel.Optimal);
                await using (var workspaceStream = workspaceEntry.Open())
                {
                    await WorkspaceBackupSerializer.WriteDocumentAsync(
                        workspaceStream,
                        WorkspaceBackupSerializer.CreateDocument(snapshot),
                        cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();

                await AddWorkspaceMediaAsync(archive, snapshot, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destinationFullPath, overwrite: true);
            return new WorkspaceBackupExportResult(destinationFullPath, snapshot.Notes.Count);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<WorkspaceBackupPreview> InspectAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        var inspectionRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(inspectionRoot);
        try
        {
            using var archive = ZipFile.OpenRead(Path.GetFullPath(packagePath));
            var validatedArchive = await ValidateArchiveAsync(archive, inspectionRoot, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new WorkspaceBackupPreview(
                validatedArchive.Document.WorkspaceName,
                validatedArchive.Document.Notes.Count,
                validatedArchive.Manifest.ExportedAtUtc);
        }
        finally
        {
            if (Directory.Exists(inspectionRoot))
            {
                Directory.Delete(inspectionRoot, recursive: true);
            }
        }
    }

    public async Task<WorkspaceBackupImportResult> ImportOverwriteAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        var mediaRoot = GetConfiguredMediaRoot();
        var importRoot = CreateImportRoot();
        var workspaceReplacementCommitted = false;
        try
        {
            EnsureImportAndMediaRootsDoNotOverlap(importRoot, mediaRoot);
            Workspace workspace;
            using (var archive = ZipFile.OpenRead(Path.GetFullPath(packagePath)))
            {
                var validatedArchive = await ValidateArchiveAsync(archive, importRoot, cancellationToken);
                workspace = WorkspaceBackupSerializer.ToWorkspace(validatedArchive.Document);
                await ExtractArchiveAsync(validatedArchive.Entries, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var stagedMediaRoot = Path.Combine(importRoot, "media");
            Directory.CreateDirectory(stagedMediaRoot);

            var rollbackRoot = CreateRollbackRoot(mediaRoot);
            var hadCurrentMedia = Directory.Exists(mediaRoot);
            if (!hadCurrentMedia && File.Exists(mediaRoot))
            {
                throw new InvalidOperationException("Configured media root is a file.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var currentMediaMoved = false;
            var stagedMediaInstalled = false;
            try
            {
                if (hadCurrentMedia)
                {
                    MoveManagedDirectory(mediaRoot, rollbackRoot, mediaRoot, rollbackRoot, importRoot);
                    currentMediaMoved = true;
                }

                MoveManagedDirectory(stagedMediaRoot, mediaRoot, mediaRoot, rollbackRoot, importRoot);
                stagedMediaInstalled = true;
                await _workspaceApplicationService.ReplaceAllAsync(workspace, cancellationToken);
                workspaceReplacementCommitted = true;
            }
            catch (Exception replacementException)
            {
                try
                {
                    if (stagedMediaInstalled && Directory.Exists(mediaRoot))
                    {
                        DeleteManagedDirectory(mediaRoot, mediaRoot, rollbackRoot, importRoot);
                    }

                    if (currentMediaMoved && Directory.Exists(rollbackRoot))
                    {
                        MoveManagedDirectory(rollbackRoot, mediaRoot, mediaRoot, rollbackRoot, importRoot);
                    }
                }
                catch (Exception restoreException)
                {
                    throw new InvalidOperationException(
                        "Workspace import failed and the previous media could not be restored.",
                        new AggregateException(replacementException, restoreException));
                }

                throw;
            }

            if (currentMediaMoved)
            {
                TryDeleteManagedDirectory(rollbackRoot, mediaRoot, rollbackRoot, importRoot);
            }

            return new WorkspaceBackupImportResult(
                workspace.Id,
                workspace.Name,
                workspace.Notes.Count);
        }
        finally
        {
            if (Directory.Exists(importRoot))
            {
                if (workspaceReplacementCommitted)
                {
                    TryDeleteManagedDirectory(importRoot, mediaRoot, null, importRoot);
                }
                else
                {
                    DeleteManagedDirectory(importRoot, mediaRoot, null, importRoot);
                }
            }
        }
    }

    private async Task AddWorkspaceMediaAsync(
        ZipArchive archive,
        WorkspaceSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        foreach (var note in snapshot.Notes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var noteDirectory = Path.Combine(_noteMediaService.MediaRoot, note.Id.Value.ToString("N"));
            if (!Directory.Exists(noteDirectory))
            {
                continue;
            }

            foreach (var sourcePath in Directory.EnumerateFiles(noteDirectory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(noteDirectory, sourcePath)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
                var entry = archive.CreateEntry(
                    $"media/{note.Id.Value:N}/{relativePath}",
                    CompressionLevel.Optimal);
                await using var source = new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 81920,
                    useAsync: true);
                await using var destination = entry.Open();
                await source.CopyToAsync(destination, cancellationToken);
            }
        }
    }

    private static ZipArchiveEntry GetRequiredRootEntry(ZipArchive archive, string name)
    {
        var matches = archive.Entries
            .Where(entry => string.Equals(entry.FullName, name, StringComparison.Ordinal))
            .ToList();
        if (matches.Count != 1)
        {
            throw new InvalidDataException($"Backup archive must contain exactly one root {name} entry.");
        }

        return matches[0];
    }

    private static async Task<WorkspaceBackupManifest> ReadManifestAsync(
        ZipArchiveEntry manifestEntry,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var manifestStream = manifestEntry.Open();
            return await JsonSerializer.DeserializeAsync<WorkspaceBackupManifest>(
                       manifestStream,
                       JsonOptions,
                       cancellationToken)
                   ?? throw new InvalidDataException("Backup manifest cannot be null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Backup manifest JSON is invalid.", exception);
        }
    }

    private static void ValidateManifest(WorkspaceBackupManifest manifest)
    {
        if (!string.Equals(manifest.Format, BackupFormat, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Backup archive format is not supported.");
        }

        if (manifest.SchemaVersion > SchemaVersion)
        {
            throw new UnsupportedWorkspaceBackupSchemaException(manifest.SchemaVersion);
        }

        if (manifest.SchemaVersion != SchemaVersion)
        {
            throw new InvalidDataException("Backup archive schema version is not supported.");
        }

        if (string.IsNullOrWhiteSpace(manifest.AppVersion))
        {
            throw new InvalidDataException("Backup manifest app version is required.");
        }

        if (manifest.ExportedAtUtc == default)
        {
            throw new InvalidDataException("Backup manifest export timestamp is required.");
        }
    }

    private static IReadOnlyList<ResolvedArchiveEntry> ResolveArchiveEntries(
        IReadOnlyCollection<ZipArchiveEntry> entries,
        string inspectionRoot)
    {
        var normalizedRoot = Path.GetFullPath(inspectionRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var resolvedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolvedEntries = new List<ResolvedArchiveEntry>(entries.Count);
        foreach (var entry in entries)
        {
            var entryPath = entry.FullName
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var resolvedPath = Path.GetFullPath(Path.Combine(inspectionRoot, entryPath));
            if (!resolvedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Backup archive contains a path outside its extraction root.");
            }

            if (!resolvedPaths.Add(GetCollisionKey(resolvedPath, inspectionRoot)))
            {
                throw new InvalidDataException("Backup archive contains entries with colliding extraction paths.");
            }

            resolvedEntries.Add(new ResolvedArchiveEntry(entry, resolvedPath));
        }

        return resolvedEntries;
    }

    private static string GetCollisionKey(string resolvedPath, string inspectionRoot)
    {
        var normalizedRoot = Path.GetFullPath(inspectionRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = resolvedPath
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase)
            ? normalizedRoot
            : normalizedPath;
    }

    private static void ValidateMediaEntries(
        IReadOnlyCollection<ResolvedArchiveEntry> entries,
        WorkspaceBackupDocument document,
        string inspectionRoot)
    {
        var noteIds = document.Notes.Select(note => note.Id).ToHashSet();
        foreach (var entry in entries)
        {
            if (entry.Entry.FullName is "manifest.json" or "workspace.json")
            {
                continue;
            }

            var entryPath = entry.Entry.FullName.Replace('\\', '/');
            var segments = entryPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2 || !string.Equals(segments[0], "media", StringComparison.Ordinal) ||
                !Guid.TryParse(segments[1], out var noteId) || !noteIds.Contains(noteId) ||
                (segments.Length == 2 && !entryPath.EndsWith("/", StringComparison.Ordinal)))
            {
                throw new InvalidDataException("Backup archive contains media outside the workspace note directories.");
            }

            var noteMediaRoot = Path.Combine(inspectionRoot, "media", noteId.ToString("N"));
            if (!IsPathWithin(entry.ResolvedPath, noteMediaRoot) &&
                !(entryPath.EndsWith("/", StringComparison.Ordinal) &&
                  string.Equals(entry.ResolvedPath, Path.GetFullPath(noteMediaRoot), StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("Backup archive media escapes its declared note directory.");
            }
        }
    }

    private static bool IsPathWithin(string path, string root)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ValidatedArchive> ValidateArchiveAsync(
        ZipArchive archive,
        string extractionRoot,
        CancellationToken cancellationToken)
    {
        var resolvedEntries = ResolveArchiveEntries(archive.Entries, extractionRoot);
        var manifestEntry = GetRequiredRootEntry(archive, "manifest.json");
        var workspaceEntry = GetRequiredRootEntry(archive, "workspace.json");
        var manifest = await ReadManifestAsync(manifestEntry, cancellationToken);
        ValidateManifest(manifest);

        WorkspaceBackupDocument document;
        await using (var workspaceStream = workspaceEntry.Open())
        {
            document = await WorkspaceBackupSerializer.ReadDocumentAsync(workspaceStream, cancellationToken);
        }

        ValidateMediaEntries(resolvedEntries, document, extractionRoot);
        cancellationToken.ThrowIfCancellationRequested();
        return new ValidatedArchive(manifest, document, resolvedEntries);
    }

    private static async Task ExtractArchiveAsync(
        IReadOnlyCollection<ResolvedArchiveEntry> entries,
        CancellationToken cancellationToken)
    {
        foreach (var resolvedEntry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsDirectoryEntry(resolvedEntry.Entry))
            {
                Directory.CreateDirectory(resolvedEntry.ResolvedPath);
                continue;
            }

            var destinationDirectory = Path.GetDirectoryName(resolvedEntry.ResolvedPath)
                ?? throw new InvalidDataException("Backup archive entry has no destination directory.");
            Directory.CreateDirectory(destinationDirectory);
            await using var source = resolvedEntry.Entry.Open();
            await using var destination = new FileStream(
                resolvedEntry.ResolvedPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            await source.CopyToAsync(destination, cancellationToken);
        }
    }

    private static bool IsDirectoryEntry(ZipArchiveEntry entry) =>
        entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
        entry.FullName.EndsWith("\\", StringComparison.Ordinal);

    private static string CreateImportRoot()
    {
        var importParent = Path.Combine(Path.GetTempPath(), "ConvenientNote", "Import");
        Directory.CreateDirectory(importParent);
        var importRoot = Path.Combine(importParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(importRoot);
        return Path.GetFullPath(importRoot);
    }

    private string GetConfiguredMediaRoot()
    {
        var configuredPath = Path.GetFullPath(_noteMediaService.MediaRoot);
        if (string.Equals(configuredPath, Path.GetPathRoot(configuredPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Configured media root must not be a filesystem root.");
        }

        var mediaRoot = configuredPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(mediaRoot) || Path.GetDirectoryName(mediaRoot) is null)
        {
            throw new InvalidOperationException("Configured media root must have a parent directory.");
        }

        return mediaRoot;
    }

    private static string CreateRollbackRoot(string mediaRoot)
    {
        var mediaParent = Path.GetDirectoryName(mediaRoot)
            ?? throw new InvalidOperationException("Configured media root must have a parent directory.");
        Directory.CreateDirectory(mediaParent);
        var rollbackRoot = Path.Combine(
            mediaParent,
            $"{Path.GetFileName(mediaRoot)}.rollback-{Guid.NewGuid():N}");
        VerifyManagedDirectoryPath(rollbackRoot, mediaRoot, rollbackRoot, null);
        return rollbackRoot;
    }

    private static void EnsureImportAndMediaRootsDoNotOverlap(string importRoot, string mediaRoot)
    {
        if (PathsEqual(importRoot, mediaRoot) || IsPathWithin(importRoot, mediaRoot) || IsPathWithin(mediaRoot, importRoot))
        {
            throw new InvalidOperationException("Configured media root must not overlap the import temporary directory.");
        }
    }

    private static void MoveManagedDirectory(
        string source,
        string destination,
        string mediaRoot,
        string? rollbackRoot,
        string importRoot)
    {
        VerifyManagedDirectoryPath(source, mediaRoot, rollbackRoot, importRoot);
        VerifyManagedDirectoryPath(destination, mediaRoot, rollbackRoot, importRoot);
        Directory.Move(source, destination);
    }

    private static void DeleteManagedDirectory(
        string path,
        string mediaRoot,
        string? rollbackRoot,
        string importRoot)
    {
        VerifyManagedDirectoryPath(path, mediaRoot, rollbackRoot, importRoot);
        Directory.Delete(path, recursive: true);
    }

    private static void TryDeleteManagedDirectory(
        string path,
        string mediaRoot,
        string? rollbackRoot,
        string importRoot)
    {
        try
        {
            DeleteManagedDirectory(path, mediaRoot, rollbackRoot, importRoot);
        }
        catch
        {
            // The workspace replacement is already committed; cleanup must not turn it into a reported failure.
        }
    }

    private static void VerifyManagedDirectoryPath(
        string path,
        string mediaRoot,
        string? rollbackRoot,
        string? importRoot)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (PathsEqual(fullPath, mediaRoot) ||
            (rollbackRoot is not null && IsValidRollbackDirectory(fullPath, mediaRoot, rollbackRoot)) ||
            (importRoot is not null && (PathsEqual(fullPath, importRoot) || IsPathWithin(fullPath, importRoot))))
        {
            return;
        }

        throw new InvalidOperationException("Import attempted a filesystem operation outside its managed directories.");
    }

    private static bool IsValidRollbackDirectory(string path, string mediaRoot, string rollbackRoot)
    {
        if (!PathsEqual(path, rollbackRoot))
        {
            return false;
        }

        var mediaParent = Path.GetDirectoryName(mediaRoot);
        var rollbackParent = Path.GetDirectoryName(path);
        var expectedPrefix = $"{Path.GetFileName(mediaRoot)}.rollback-";
        var rollbackName = Path.GetFileName(path);
        return mediaParent is not null &&
               rollbackParent is not null &&
               string.Equals(mediaParent, rollbackParent, StringComparison.OrdinalIgnoreCase) &&
               rollbackName.StartsWith(expectedPrefix, StringComparison.Ordinal) &&
               Guid.TryParseExact(rollbackName[expectedPrefix.Length..], "N", out _);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private sealed record ResolvedArchiveEntry(ZipArchiveEntry Entry, string ResolvedPath);

    private sealed record ValidatedArchive(
        WorkspaceBackupManifest Manifest,
        WorkspaceBackupDocument Document,
        IReadOnlyList<ResolvedArchiveEntry> Entries);
}
