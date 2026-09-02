using System.IO;

namespace ConvenientNote.Services;

internal sealed class NotesBackupPackageSnapshot : IDisposable, IAsyncDisposable
{
    private string? _snapshotRoot;
    private FileStream? _packageLease;
    private readonly Action<string> _deleteSnapshotRoot;

    internal NotesBackupPackageSnapshot(
        string snapshotRoot,
        string packagePath,
        Action<string>? deleteSnapshotRoot = null,
        FileStream? packageLease = null)
    {
        _snapshotRoot = snapshotRoot;
        _packageLease = packageLease;
        PackagePath = packagePath;
        _deleteSnapshotRoot = deleteSnapshotRoot ?? NotesBackupPackageStager.DeleteSnapshotRoot;
    }

    public string PackagePath { get; }

    public void Dispose()
    {
        var packageLease = Interlocked.Exchange(ref _packageLease, null);
        try
        {
            packageLease?.Dispose();
        }
        catch
        {
            // Releasing the immutable-file lease is best effort during snapshot cleanup.
        }

        var snapshotRoot = Interlocked.Exchange(ref _snapshotRoot, null);
        NotesBackupPackageStager.TryDeleteSnapshotRoot(snapshotRoot, _deleteSnapshotRoot);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class NotesBackupPackageStager
{
    internal Task<NotesBackupPackageSnapshot> StageAsync(
        string sourcePath,
        CancellationToken cancellationToken = default) =>
        StageAsync(sourcePath, cancellationToken, null, null);

    internal async Task<NotesBackupPackageSnapshot> StageAsync(
        string sourcePath,
        CancellationToken cancellationToken,
        Action<string>? deleteSnapshotRoot,
        Func<Stream, Stream, CancellationToken, Task>? copyAsync = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var snapshotRoot = Path.Combine(
            Path.GetTempPath(),
            "ConvenientNote",
            "Import",
            "SelectedNotesPackage",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(snapshotRoot);
        var snapshotPath = Path.Combine(snapshotRoot, "notes.cnote");
        var deleteRoot = deleteSnapshotRoot ?? DeleteSnapshotRoot;
        var copy = copyAsync ?? (static (source, destination, token) => source.CopyToAsync(destination, token));
        FileStream? packageLease = null;
        try
        {
            await using var source = new FileStream(
                Path.GetFullPath(sourcePath),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);
            await using (var destination = new FileStream(
                             snapshotPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             useAsync: true))
            {
                await copy(source, destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            packageLease = new FileStream(
                snapshotPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1,
                useAsync: false);
            return new NotesBackupPackageSnapshot(snapshotRoot, snapshotPath, deleteRoot, packageLease);
        }
        catch
        {
            if (packageLease is not null)
            {
                try
                {
                    await packageLease.DisposeAsync();
                }
                catch
                {
                    // Lease cleanup must not mask cancellation or copy failures.
                }
            }

            TryDeleteSnapshotRoot(snapshotRoot, deleteRoot);
            throw;
        }
    }

    internal static void TryDeleteSnapshotRoot(string? snapshotRoot, Action<string> deleteSnapshotRoot)
    {
        if (snapshotRoot is null || !Directory.Exists(snapshotRoot))
        {
            return;
        }

        try
        {
            deleteSnapshotRoot(snapshotRoot);
        }
        catch
        {
            // Staged package cleanup is best effort and must not mask the primary outcome.
        }
    }

    internal static void DeleteSnapshotRoot(string path) => Directory.Delete(path, recursive: true);
}
