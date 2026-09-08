using ConvenientNote.Infrastructure.Persistence;
using ConvenientNote.Notes.Infrastructure;
using ConvenientNote.Platform.Contracts;
using ConvenientNote.Platform.Infrastructure;
using ConvenientNote.Todos.Domain;
using ConvenientNote.Todos.Infrastructure;
using Microsoft.Data.Sqlite;
using LegacyNote = ConvenientNote.Domain.Notes.Note;
using NotesDomain = ConvenientNote.Notes.Domain.Notes;

namespace ConvenientNote.LegacyMigration;

/// <summary>The only production boundary allowed to understand the retired mixed Note model.</summary>
public sealed class LegacyDataMigration(string dataDirectory)
{
    public const string Version = "modular-ddd-v1";
    public string DatabasePath => Path.Combine(Path.GetFullPath(dataDirectory), "ConvenientNote.Modules.db");

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(directory);
        // Serializes concurrent initializers across processes. No UI can write before this completes.
        await using var migrationLock = new FileStream(Path.Combine(directory, ".module-migration.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var workspaces = new SqliteWorkspaceContext(DatabasePath);
        if (await workspaces.HasMigrationAsync(Version, cancellationToken).ConfigureAwait(false)) return;

        var snapshotDirectory = Path.Combine(directory, "MigrationBackup-v1");
        Directory.CreateDirectory(snapshotDirectory);
        await PrepareSnapshotAsync(directory, snapshotDirectory, cancellationToken).ConfigureAwait(false);
        var legacy = new SqliteWorkspaceRepository(Path.Combine(snapshotDirectory, "ConvenientNote.db"));
        var snapshots = await legacy.ListAsync(cancellationToken).ConfigureAwait(false);
        // Unknown records are not silently discarded. The original database remains untouched.
        var unknown = snapshots.SelectMany(w => w.Notes).FirstOrDefault(n => n.BoardKey is not ("testing" or "day-todo"));
        if (unknown is not null) throw new InvalidDataException($"发现无法迁移的数据分类：{unknown.BoardKey}。原数据已保留。");

        var identities = snapshots.Select(w => new WorkspaceInfo(w.Id.Value, w.Name)).ToArray();
        if (identities.Length == 0)
        {
            // Reuse an identity from a previous incomplete fresh initialization.
            WorkspaceInfo identity;
            try { identity = await workspaces.GetCurrentAsync(cancellationToken).ConfigureAwait(false); }
            catch (InvalidOperationException) { identity = new WorkspaceInfo(Guid.NewGuid(), "默认工作区"); }
            identities = [identity];
        }
        await workspaces.ImportWorkspacesAsync(identities, cancellationToken).ConfigureAwait(false);
        var notes = new SqliteNotesRepository(DatabasePath);
        var todos = new SqliteTodoRepository(DatabasePath);
        foreach (var workspace in snapshots)
        {
            await notes.ImportLegacyAsync(workspace.Id.Value,
                workspace.Notes.Where(n => n.BoardKey == "testing").Select(ToNote).ToArray(), cancellationToken).ConfigureAwait(false);
            await todos.ImportLegacyAsync(workspace.Id.Value,
                workspace.Notes.Where(n => n.BoardKey == "day-todo").Select(ToTodo).ToArray(), cancellationToken).ConfigureAwait(false);
        }
        await CopyLegacyMediaAsync(directory, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await workspaces.CompleteMigrationAsync(Version, cancellationToken).ConfigureAwait(false);
    }

    private static NotesDomain.Note ToNote(LegacyNote note) => new(
        new NotesDomain.NoteId(note.Id.Value), note.Title, note.Content,
        new NotesDomain.NotePosition(note.Position.X, note.Position.Y),
        new NotesDomain.NoteSize(note.Size.Width, note.Size.Height), note.Color, note.ZIndex,
        note.CreatedAt, note.UpdatedAt, note.RichContent,
        note.NotebookId is { } notebook ? new NotesDomain.NotebookId(notebook.Value) : null,
        note.Tags, note.IsPinned, note.IsFavorite, note.IsDeleted,
        new NotesDomain.LegacyNoteMetadata(note.Priority, note.IsCompleted, note.PlannedDate, note.CompletedAt));

    private static TodoItem ToTodo(LegacyNote note) => TodoItem.Restore(new TodoId(note.Id.Value),
        note.Title, note.Content, note.Priority, note.Position.X, note.Position.Y,
        note.Size.Width, note.Size.Height, note.Color, note.ZIndex, note.IsCompleted, note.IsDeleted,
        note.CreatedAt, note.UpdatedAt, note.PlannedDate, note.CompletedAt);

    private static async Task CopyLegacyMediaAsync(string dataRoot, CancellationToken ct)
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(dataRoot, "Media"));
        var destinationRoot = Path.GetFullPath(Path.Combine(dataRoot, "Notes", "Media"));
        EnsureContainedPath(dataRoot, sourceRoot);
        EnsureContainedPath(dataRoot, destinationRoot);
        EnsureNoReparsePoints(dataRoot, sourceRoot);
        EnsureNoReparsePoints(dataRoot, destinationRoot);
        if (!Directory.Exists(sourceRoot)) return;

        Directory.CreateDirectory(destinationRoot);
        EnsureNoReparsePoints(dataRoot, destinationRoot);
        var pending = new Stack<string>();
        pending.Push(sourceRoot);
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            EnsureNoReparsePoints(sourceRoot, directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                ct.ThrowIfCancellationRequested();
                EnsureContainedPath(sourceRoot, entry);
                EnsureNoReparsePoints(sourceRoot, entry);
                if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }

                var relativePath = Path.GetRelativePath(sourceRoot, entry);
                var destination = Path.GetFullPath(Path.Combine(destinationRoot, relativePath));
                EnsureContainedPath(destinationRoot, destination);
                EnsureNoReparsePoints(destinationRoot, destination);
                // Completed copies survive retries and are never replaced by stale legacy media.
                if (File.Exists(destination)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                EnsureNoReparsePoints(destinationRoot, destination);
                var temporary = destination + ".migration-" + Guid.NewGuid().ToString("N");
                EnsureContainedPath(destinationRoot, temporary);
                try
                {
                    await using (var input = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.Read,
                        81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        await input.CopyToAsync(output, ct).ConfigureAwait(false);
                        await output.FlushAsync(ct).ConfigureAwait(false);
                        output.Flush(flushToDisk: true);
                    }
                    ct.ThrowIfCancellationRequested();
                    EnsureNoReparsePoints(destinationRoot, destination);
                    // Same-directory rename exposes only a complete file and never overwrites an existing one.
                    File.Move(temporary, destination, overwrite: false);
                }
                finally
                {
                    EnsureNoReparsePoints(destinationRoot, temporary);
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
        }
    }

    private static void EnsureContainedPath(string root, string path)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("媒体迁移路径超出允许目录。");
    }

    private static void EnsureNoReparsePoints(string root, string path)
    {
        EnsureContainedPath(root, path);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var current = Path.GetFullPath(path);
        while (true)
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"媒体迁移不支持链接或重解析点：{current}");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            if (current.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)) return;
            current = Path.GetDirectoryName(current)
                ?? throw new InvalidDataException("无法验证媒体迁移路径。");
        }
    }

    private static async Task PrepareSnapshotAsync(string sourceDirectory, string backupDirectory, CancellationToken ct)
    {
        var ready = Path.Combine(backupDirectory, "snapshot.ready");
        if (File.Exists(ready)) return;
        var sourcePath = Path.Combine(sourceDirectory, "ConvenientNote.db");
        var backupPath = Path.Combine(backupDirectory, "ConvenientNote.db");
        if (File.Exists(sourcePath))
        {
            await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
            await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupPath }.ToString());
            await source.OpenAsync(ct).ConfigureAwait(false);
            await destination.OpenAsync(ct).ConfigureAwait(false);
            // SQLite's online backup includes committed WAL data; copying the db file alone does not.
            ct.ThrowIfCancellationRequested();
            source.BackupDatabase(destination);
        }
        var jsonPath = Path.Combine(sourceDirectory, "workspaces.json");
        if (File.Exists(jsonPath)) File.Copy(jsonPath, Path.Combine(backupDirectory, "workspaces.json"), true);
        await File.WriteAllTextAsync(ready, Version, ct).ConfigureAwait(false);
    }
}

