using System.IO;
using System.IO.Compression;
using System.Text.Json;
using ConvenientNote.Application.Workspaces;

namespace ConvenientNote.Services;

public sealed class NotesBackupService
{
    private const string BackupFormat = "convenient-note-notes-backup";
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true
    };

    private readonly WorkspaceApplicationService _workspaceApplicationService;
    private readonly NoteMediaService _noteMediaService;
    private readonly Action<string> _deleteTemporaryFile;
    private readonly Action<string, string> _moveDirectory;
    private readonly NotesBackupArchiveLimits _limits;
    private readonly Func<ZipArchiveEntry, long> _getDeclaredExpandedLength;

    public NotesBackupService(
        WorkspaceApplicationService workspaceApplicationService,
        NoteMediaService noteMediaService)
        : this(workspaceApplicationService, noteMediaService, File.Delete)
    {
    }

    internal NotesBackupService(
        WorkspaceApplicationService workspaceApplicationService,
        NoteMediaService noteMediaService,
        Action<string> deleteTemporaryFile,
        Action<string, string>? moveDirectory = null,
        NotesBackupArchiveLimits? limits = null,
        Func<ZipArchiveEntry, long>? getDeclaredExpandedLength = null)
    {
        _workspaceApplicationService = workspaceApplicationService;
        _noteMediaService = noteMediaService;
        _deleteTemporaryFile = deleteTemporaryFile;
        _moveDirectory = moveDirectory ?? Directory.Move;
        _limits = limits ?? NotesBackupArchiveLimits.Default;
        _getDeclaredExpandedLength = getDeclaredExpandedLength ?? (static entry => entry.Length);
    }

    public async Task<NotesBackupExportResult> ExportAsync(
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
            var workspace = await _workspaceApplicationService
                .GetOrCreateDefaultWorkspaceAsync(cancellationToken);
            var document = NotesBackupSerializer.CreateDocument(workspace.Notes);
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
                var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                await using (var manifestStream = manifestEntry.Open())
                {
                    await JsonSerializer.SerializeAsync(
                        manifestStream,
                        new NotesBackupManifest(
                            BackupFormat,
                            SchemaVersion,
                            "1.0.0",
                            DateTimeOffset.UtcNow),
                        JsonOptions,
                        cancellationToken);
                }

                var notesEntry = archive.CreateEntry("notes.json", CompressionLevel.Optimal);
                await using (var notesStream = notesEntry.Open())
                {
                    await NotesBackupSerializer.WriteDocumentAsync(
                        notesStream,
                        document,
                        cancellationToken);
                }

                await AddNotesMediaAsync(archive, document, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destinationFullPath, overwrite: true);
            return new NotesBackupExportResult(destinationFullPath, document.Notes.Count);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    public async Task<NotesBackupPreview> InspectAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        var inspectionRoot = CreateInspectionRoot();
        try
        {
            using var archive = ZipFile.OpenRead(Path.GetFullPath(packagePath));
            var readBudget = new ArchiveReadBudget(_limits.MaximumTotalExpandedBytes);
            var validatedArchive = await ValidateArchiveAsync(
                archive,
                inspectionRoot,
                readBudget,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new NotesBackupPreview(
                validatedArchive.Document.Notes.Count,
                validatedArchive.Manifest.ExportedAtUtc);
        }
        finally
        {
            TryDeleteDirectory(inspectionRoot);
        }
    }

    public async Task<NotesBackupImportResult> ImportOverwriteAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        return await ImportOverwriteAsync(packagePath, static () => { }, cancellationToken);
    }

    internal async Task<NotesBackupImportResult> ImportOverwriteAsync(
        string packagePath,
        Action replacementCommitted,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(replacementCommitted);

        var mediaRoot = GetConfiguredMediaRoot();
        var mediaRootExistedBeforeImport = Directory.Exists(mediaRoot);
        var importRoot = CreateImportInstallRoot(mediaRoot);
        string? rollbackRoot = null;
        var databaseCommitted = false;
        var mediaRootCreatedForImport = false;
        var destructiveMediaMoveStarted = false;
        var unrestoredRollbackDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            IReadOnlyList<ConvenientNote.Domain.Notes.Note> importedNotes;
            using (var archive = ZipFile.OpenRead(Path.GetFullPath(packagePath)))
            {
                var readBudget = new ArchiveReadBudget(_limits.MaximumTotalExpandedBytes);
                var validatedArchive = await ValidateArchiveAsync(
                    archive,
                    importRoot,
                    readBudget,
                    cancellationToken);
                importedNotes = NotesBackupSerializer.ToNotes(validatedArchive.Document);
                await ExtractArchiveAsync(validatedArchive.Entries, readBudget, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var currentWorkspace = await _workspaceApplicationService
                .GetOrCreateDefaultWorkspaceAsync(cancellationToken);
            ValidateNoPreservedIdCollisions(currentWorkspace, importedNotes);

            if (File.Exists(mediaRoot))
            {
                throw new InvalidOperationException("Configured media root is a file.");
            }

            if (!Directory.Exists(mediaRoot))
            {
                Directory.CreateDirectory(mediaRoot);
                mediaRootCreatedForImport = !mediaRootExistedBeforeImport;
            }
            var oldActiveIds = currentWorkspace.Notes
                .Where(static note => note.BoardKey == TodoBoardKeys.Notes && !note.IsDeleted)
                .Select(note => note.Id.Value)
                .Distinct()
                .ToHashSet();
            foreach (var importedNote in importedNotes)
            {
                var destination = GetNoteDirectory(mediaRoot, importedNote.Id.Value);
                if (!oldActiveIds.Contains(importedNote.Id.Value) &&
                    (Directory.Exists(destination) || File.Exists(destination)))
                {
                    throw new InvalidOperationException("Imported note media collides with unowned local media.");
                }
            }

            var stagedMediaRoot = Path.Combine(importRoot, "media");
            Directory.CreateDirectory(stagedMediaRoot);
            rollbackRoot = CreateRollbackRoot(mediaRoot);
            Directory.CreateDirectory(rollbackRoot);

            var movedOldDirectories = new List<MovedDirectory>();
            var installedDirectories = new List<string>();
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var oldActiveId in oldActiveIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var source = GetNoteDirectory(mediaRoot, oldActiveId);
                    if (!Directory.Exists(source))
                    {
                        continue;
                    }

                    var rollbackDestination = GetNoteDirectory(rollbackRoot, oldActiveId);
                    destructiveMediaMoveStarted = true;
                    _moveDirectory(source, rollbackDestination);
                    movedOldDirectories.Add(new MovedDirectory(source, rollbackDestination));
                    unrestoredRollbackDirectories.Add(rollbackDestination);
                }

                foreach (var stagedDirectory in Directory.EnumerateDirectories(stagedMediaRoot))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var directoryName = Path.GetFileName(stagedDirectory);
                    if (!Guid.TryParseExact(directoryName, "N", out var importedId))
                    {
                        throw new InvalidDataException("Staged notes media contains an invalid note directory.");
                    }

                    var destination = GetNoteDirectory(mediaRoot, importedId);
                    if (Directory.Exists(destination) || File.Exists(destination))
                    {
                        throw new InvalidOperationException("Imported note media collides with preserved local media.");
                    }

                    destructiveMediaMoveStarted = true;
                    _moveDirectory(stagedDirectory, destination);
                    installedDirectories.Add(destination);
                }

                cancellationToken.ThrowIfCancellationRequested();
                await _workspaceApplicationService.CommitActiveNotesReplacementAsync(
                    currentWorkspace.Id,
                    importedNotes,
                    cancellationToken,
                    () =>
                    {
                        databaseCommitted = true;
                        replacementCommitted();
                    });
            }
            catch (Exception replacementException)
            {
                if (databaseCommitted)
                {
                    throw;
                }

                var restoreExceptions = new List<Exception>();
                for (var index = installedDirectories.Count - 1; index >= 0; index--)
                {
                    try
                    {
                        if (Directory.Exists(installedDirectories[index]))
                        {
                            Directory.Delete(installedDirectories[index], recursive: true);
                        }
                    }
                    catch (Exception exception)
                    {
                        restoreExceptions.Add(exception);
                    }
                }

                for (var index = movedOldDirectories.Count - 1; index >= 0; index--)
                {
                    var moved = movedOldDirectories[index];
                    try
                    {
                        if (Directory.Exists(moved.RollbackPath))
                        {
                            _moveDirectory(moved.RollbackPath, moved.OriginalPath);
                            unrestoredRollbackDirectories.Remove(moved.RollbackPath);
                        }
                    }
                    catch (Exception exception)
                    {
                        restoreExceptions.Add(exception);
                    }
                }

                if (restoreExceptions.Count > 0)
                {
                    throw new InvalidOperationException(
                        "Notes import failed and previous note media could not be restored.",
                        new AggregateException([replacementException, .. restoreExceptions]));
                }

                throw;
            }

            return new NotesBackupImportResult(importedNotes.Count);
        }
        finally
        {
            if (rollbackRoot is not null &&
                (databaseCommitted || unrestoredRollbackDirectories.Count == 0))
            {
                TryDeleteDirectory(rollbackRoot);
            }

            TryDeleteDirectory(importRoot);
            if (mediaRootCreatedForImport && !destructiveMediaMoveStarted && !databaseCommitted)
            {
                TryDeleteNewEmptyMediaRoot(mediaRoot);
            }
        }
    }

    private async Task AddNotesMediaAsync(
        ZipArchive archive,
        NotesBackupDocument document,
        CancellationToken cancellationToken)
    {
        foreach (var note in document.Notes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var noteDirectory = Path.Combine(_noteMediaService.MediaRoot, note.Id.ToString("N"));
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
                    $"media/{note.Id:N}/{relativePath}",
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
            throw new InvalidDataException($"Notes backup must contain exactly one root {name} entry.");
        }

        return matches[0];
    }

    private async Task<NotesBackupManifest> ReadManifestAsync(
        ZipArchiveEntry manifestEntry,
        ArchiveReadBudget readBudget,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var manifestStream = new BoundedArchiveReadStream(
                manifestEntry.Open(),
                _limits.MaximumManifestBytes,
                readBudget);
            return await JsonSerializer.DeserializeAsync<NotesBackupManifest>(
                       manifestStream,
                       JsonOptions,
                       cancellationToken)
                   ?? throw new InvalidDataException("Notes backup manifest cannot be null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Notes backup manifest JSON is invalid.", exception);
        }
    }

    private static void ValidateManifest(NotesBackupManifest manifest)
    {
        if (!string.Equals(manifest.Format, BackupFormat, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Notes backup format is not supported.");
        }

        if (manifest.SchemaVersion > SchemaVersion)
        {
            throw new UnsupportedNotesBackupSchemaException(manifest.SchemaVersion);
        }

        if (manifest.SchemaVersion != SchemaVersion)
        {
            throw new InvalidDataException("Notes backup schema version is not supported.");
        }

        if (string.IsNullOrWhiteSpace(manifest.AppVersion))
        {
            throw new InvalidDataException("Notes backup app version is required.");
        }

        if (manifest.ExportedAtUtc == default)
        {
            throw new InvalidDataException("Notes backup export timestamp is required.");
        }
    }

    private async Task<ValidatedArchive> ValidateArchiveAsync(
        ZipArchive archive,
        string extractionRoot,
        ArchiveReadBudget readBudget,
        CancellationToken cancellationToken)
    {
        if (archive.Entries.Count > _limits.MaximumEntryCount)
        {
            throw new InvalidDataException("Notes backup ZIP entry count exceeds the archive limit.");
        }

        var entries = ResolveArchiveEntries(archive.Entries, extractionRoot);
        var manifestEntry = GetRequiredRootEntry(archive, "manifest.json");
        var notesEntry = GetRequiredRootEntry(archive, "notes.json");
        ValidateArchiveMetadata(archive.Entries, manifestEntry, notesEntry);
        var manifest = await ReadManifestAsync(manifestEntry, readBudget, cancellationToken);
        ValidateManifest(manifest);

        NotesBackupDocument document;
        await using (var notesStream = new BoundedArchiveReadStream(
                         notesEntry.Open(),
                         _limits.MaximumNotesJsonBytes,
                         readBudget))
        {
            document = await NotesBackupSerializer.ReadDocumentAsync(notesStream, cancellationToken);
        }

        if (document.Notes.Count > _limits.MaximumNoteCount)
        {
            throw new InvalidDataException("Notes backup note count exceeds the archive limit.");
        }

        ValidateMediaEntries(entries, document, extractionRoot);
        cancellationToken.ThrowIfCancellationRequested();
        return new ValidatedArchive(manifest, document, entries);
    }

    private async Task ExtractArchiveAsync(
        IReadOnlyCollection<ResolvedArchiveEntry> entries,
        ArchiveReadBudget readBudget,
        CancellationToken cancellationToken)
    {
        foreach (var resolvedEntry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (resolvedEntry.Entry.FullName is "manifest.json" or "notes.json")
            {
                continue;
            }

            if (IsDirectoryEntry(resolvedEntry.Entry))
            {
                Directory.CreateDirectory(resolvedEntry.ResolvedPath);
                continue;
            }

            var destinationDirectory = Path.GetDirectoryName(resolvedEntry.ResolvedPath)
                ?? throw new InvalidDataException("Notes backup entry has no destination directory.");
            Directory.CreateDirectory(destinationDirectory);
            await using var source = new BoundedArchiveReadStream(
                resolvedEntry.Entry.Open(),
                _limits.MaximumMediaEntryBytes,
                readBudget);
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

    private static void ValidateNoPreservedIdCollisions(
        WorkspaceSnapshot currentWorkspace,
        IReadOnlyCollection<ConvenientNote.Domain.Notes.Note> importedNotes)
    {
        var preservedIds = currentWorkspace.Notes
            .Where(static note => note.BoardKey != TodoBoardKeys.Notes || note.IsDeleted)
            .Select(note => note.Id.Value)
            .ToHashSet();
        if (importedNotes.Any(note => preservedIds.Contains(note.Id.Value)))
        {
            throw new InvalidDataException("An imported note ID collides with a preserved record.");
        }
    }

    private IReadOnlyList<ResolvedArchiveEntry> ResolveArchiveEntries(
        IReadOnlyCollection<ZipArchiveEntry> entries,
        string extractionRoot)
    {
        var normalizedRoot = Path.GetFullPath(extractionRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var pathIndex = new NotesBackupArchivePathIndex(extractionRoot);
        var resolvedEntries = new List<ResolvedArchiveEntry>(entries.Count);
        foreach (var entry in entries)
        {
            var entryPath = entry.FullName
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var resolvedPath = Path.GetFullPath(Path.Combine(extractionRoot, entryPath));
            if (!resolvedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Notes backup contains a path outside its extraction root.");
            }

            var normalizedRelativePath = Path.GetRelativePath(extractionRoot, resolvedPath);
            var normalizedDepth = normalizedRelativePath.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries).Length;
            if (normalizedDepth > _limits.MaximumNormalizedPathDepth)
            {
                throw new InvalidDataException("Notes backup normalized path depth exceeds the archive limit.");
            }

            pathIndex.Add(resolvedPath, IsDirectoryEntry(entry));
            resolvedEntries.Add(new ResolvedArchiveEntry(entry, resolvedPath));
        }

        pathIndex.ValidateAncestors();
        return resolvedEntries;
    }

    private void ValidateArchiveMetadata(
        IReadOnlyCollection<ZipArchiveEntry> entries,
        ZipArchiveEntry manifestEntry,
        ZipArchiveEntry notesEntry)
    {
        long totalExpandedBytes = 0;
        foreach (var entry in entries)
        {
            var declaredExpandedBytes = _getDeclaredExpandedLength(entry);
            if (declaredExpandedBytes < 0)
            {
                throw new InvalidDataException("Notes backup contains an invalid expanded-size limit value.");
            }

            if (declaredExpandedBytes > _limits.MaximumTotalExpandedBytes - totalExpandedBytes)
            {
                throw new InvalidDataException("Notes backup total expanded bytes exceed the archive limit.");
            }

            totalExpandedBytes += declaredExpandedBytes;
            if (ReferenceEquals(entry, manifestEntry) && declaredExpandedBytes > _limits.MaximumManifestBytes)
            {
                throw new InvalidDataException("Notes backup manifest bytes exceed the archive limit.");
            }

            if (ReferenceEquals(entry, notesEntry) && declaredExpandedBytes > _limits.MaximumNotesJsonBytes)
            {
                throw new InvalidDataException("Notes backup notes JSON bytes exceed the archive limit.");
            }

            if (!ReferenceEquals(entry, manifestEntry) &&
                !ReferenceEquals(entry, notesEntry) &&
                !IsDirectoryEntry(entry) &&
                declaredExpandedBytes > _limits.MaximumMediaEntryBytes)
            {
                throw new InvalidDataException("Notes backup media entry bytes exceed the archive limit.");
            }

            if (declaredExpandedBytes == 0)
            {
                continue;
            }

            if (entry.CompressedLength == 0 ||
                declaredExpandedBytes / (double)entry.CompressedLength > _limits.MaximumCompressionRatio)
            {
                throw new InvalidDataException("Notes backup compression ratio exceeds the archive limit.");
            }
        }
    }

    private static void ValidateMediaEntries(
        IReadOnlyCollection<ResolvedArchiveEntry> entries,
        NotesBackupDocument document,
        string extractionRoot)
    {
        var noteIds = document.Notes.Select(note => note.Id).ToHashSet();
        foreach (var entry in entries)
        {
            if (entry.Entry.FullName is "manifest.json" or "notes.json")
            {
                continue;
            }

            var entryPath = entry.Entry.FullName.Replace('\\', '/');
            var segments = entryPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2 ||
                !string.Equals(segments[0], "media", StringComparison.Ordinal) ||
                !Guid.TryParseExact(segments[1], "N", out var noteId) ||
                !noteIds.Contains(noteId) ||
                (segments.Length == 2 && !IsDirectoryEntry(entry.Entry)))
            {
                throw new InvalidDataException("Notes backup contains media outside exported note directories.");
            }

            var noteMediaRoot = Path.Combine(extractionRoot, "media", noteId.ToString("N"));
            if (!IsPathWithin(entry.ResolvedPath, noteMediaRoot) &&
                !(IsDirectoryEntry(entry.Entry) && PathsEqual(entry.ResolvedPath, noteMediaRoot)))
            {
                throw new InvalidDataException("Notes backup media escapes its declared note directory.");
            }
        }
    }

    private static bool IsDirectoryEntry(ZipArchiveEntry entry) =>
        entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
        entry.FullName.EndsWith("\\", StringComparison.Ordinal);

    private static bool IsPathWithin(string path, string root)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static string CreateInspectionRoot()
    {
        var importRoot = Path.Combine(
            Path.GetTempPath(),
            "ConvenientNote",
            "Import",
            "Notes",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(importRoot);
        return Path.GetFullPath(importRoot);
    }

    private static string CreateImportInstallRoot(string mediaRoot)
    {
        var mediaParent = Path.GetDirectoryName(mediaRoot)
            ?? throw new InvalidOperationException("Configured media root must have a parent directory.");
        Directory.CreateDirectory(mediaParent);
        var importRoot = Path.Combine(
            mediaParent,
            $"{Path.GetFileName(mediaRoot)}.import-{Guid.NewGuid():N}");
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
        return Path.Combine(
            mediaParent,
            $"{Path.GetFileName(mediaRoot)}.rollback-{Guid.NewGuid():N}");
    }

    private static string GetNoteDirectory(string root, Guid noteId)
    {
        var directory = Path.GetFullPath(Path.Combine(root, noteId.ToString("N")));
        if (!IsPathWithin(directory, root))
        {
            throw new InvalidOperationException("Note media path escaped its managed root.");
        }

        return directory;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Temporary cleanup is best effort and must not mask the primary outcome.
        }
    }

    private static void TryDeleteNewEmptyMediaRoot(string mediaRoot)
    {
        try
        {
            // An empty root is the only state this import created before its first media move.
            // Any file or directory means another actor modified it, so it must be preserved.
            if (Directory.Exists(mediaRoot) &&
                !Directory.EnumerateFileSystemEntries(mediaRoot).Any())
            {
                Directory.Delete(mediaRoot, recursive: false);
            }
        }
        catch
        {
            // Cleanup is best effort and cannot mask cancellation or validation failures.
        }
    }

    private void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                _deleteTemporaryFile(path);
            }
        }
        catch
        {
            // Temporary export cleanup is best effort and must not mask the primary outcome.
        }
    }

    private sealed class ArchiveReadBudget(long maximumBytes)
    {
        public long RemainingBytes { get; private set; } = maximumBytes;

        public void Consume(int byteCount)
        {
            if (byteCount > RemainingBytes)
            {
                throw new InvalidDataException("Notes backup streaming byte limit was exceeded.");
            }

            RemainingBytes -= byteCount;
        }
    }

    private sealed class BoundedArchiveReadStream(
        Stream inner,
        long maximumEntryBytes,
        ArchiveReadBudget totalBudget) : Stream
    {
        private long _entryBytesRead;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var allowedCount = GetAllowedReadCount(count);
            if (allowedCount == 0)
            {
                return 0;
            }

            var bytesRead = inner.Read(buffer, offset, allowedCount);
            AccountForRead(bytesRead);
            return bytesRead;
        }

        public override int Read(Span<byte> buffer)
        {
            var allowedCount = GetAllowedReadCount(buffer.Length);
            if (allowedCount == 0)
            {
                return 0;
            }

            var bytesRead = inner.Read(buffer[..allowedCount]);
            AccountForRead(bytesRead);
            return bytesRead;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var allowedCount = GetAllowedReadCount(buffer.Length);
            if (allowedCount == 0)
            {
                return 0;
            }

            var bytesRead = await inner.ReadAsync(buffer[..allowedCount], cancellationToken);
            AccountForRead(bytesRead);
            return bytesRead;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int ReadByte()
        {
            var buffer = new byte[1];
            return Read(buffer, 0, 1) == 0 ? -1 : buffer[0];
        }

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private int GetAllowedReadCount(int requestedCount)
        {
            if (requestedCount == 0)
            {
                return 0;
            }

            var entryRemaining = maximumEntryBytes - _entryBytesRead;
            var maximumProbe = Math.Min(entryRemaining, totalBudget.RemainingBytes) + 1;
            return (int)Math.Min(requestedCount, Math.Min(maximumProbe, int.MaxValue));
        }

        private void AccountForRead(int byteCount)
        {
            if (byteCount == 0)
            {
                return;
            }

            if (byteCount > maximumEntryBytes - _entryBytesRead)
            {
                throw new InvalidDataException("Notes backup streaming byte limit was exceeded.");
            }

            totalBudget.Consume(byteCount);
            _entryBytesRead += byteCount;
        }
    }

    private sealed record ResolvedArchiveEntry(ZipArchiveEntry Entry, string ResolvedPath);

    private sealed record ValidatedArchive(
        NotesBackupManifest Manifest,
        NotesBackupDocument Document,
        IReadOnlyList<ResolvedArchiveEntry> Entries);

    private sealed record MovedDirectory(string OriginalPath, string RollbackPath);
}

internal sealed class NotesBackupArchivePathIndex
{
    private readonly string _root;
    private readonly IEqualityComparer<string> _comparer;
    private readonly Dictionary<string, bool> _entries;

    internal NotesBackupArchivePathIndex(
        string root,
        IEqualityComparer<string>? comparer = null)
    {
        _root = NormalizePath(root);
        _comparer = comparer ?? StringComparer.OrdinalIgnoreCase;
        _entries = new Dictionary<string, bool>(_comparer);
    }

    internal void Add(string path, bool isDirectory)
    {
        var canonicalPath = NormalizePath(path);
        if (!_entries.TryAdd(canonicalPath, isDirectory))
        {
            throw new InvalidDataException("Notes backup contains entries with colliding extraction paths.");
        }
    }

    internal void ValidateAncestors()
    {
        foreach (var path in _entries.Keys)
        {
            var ancestor = Path.GetDirectoryName(path);
            while (ancestor is not null && !_comparer.Equals(ancestor, _root))
            {
                if (_entries.TryGetValue(ancestor, out var isDirectory) && !isDirectory)
                {
                    throw new InvalidDataException("Notes backup contains conflicting file and directory paths.");
                }

                ancestor = Path.GetDirectoryName(ancestor);
            }
        }
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
}
