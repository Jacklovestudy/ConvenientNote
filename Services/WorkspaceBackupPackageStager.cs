using System.IO;

namespace ConvenientNote.Services;

internal sealed class WorkspaceBackupPackageSnapshot : IDisposable, IAsyncDisposable
{
    private string? _snapshotRoot;
    private readonly Action<string> _deleteSnapshotRoot;

    internal WorkspaceBackupPackageSnapshot(
        string snapshotRoot,
        string packagePath,
        Action<string>? deleteSnapshotRoot = null)
    {
        _snapshotRoot = snapshotRoot;
        PackagePath = packagePath;
        _deleteSnapshotRoot = deleteSnapshotRoot ?? WorkspaceBackupPackageStager.DeleteSnapshotRoot;
    }

    public string PackagePath { get; }

    public void Dispose()
    {
        var snapshotRoot = Interlocked.Exchange(ref _snapshotRoot, null);
        WorkspaceBackupPackageStager.TryDeleteSnapshotRoot(snapshotRoot, _deleteSnapshotRoot);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

internal static class WorkspaceBackupPackageStager
{
    public static async Task<WorkspaceBackupPackageSnapshot> StageAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        return await StageAsync(sourcePath, cancellationToken, null, null);
    }

    internal static async Task<WorkspaceBackupPackageSnapshot> StageAsync(
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
            "SelectedPackage",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(snapshotRoot);
        var snapshotPath = Path.Combine(snapshotRoot, "workspace.cnote");
        var deleteRoot = deleteSnapshotRoot ?? DeleteSnapshotRoot;
        var copy = copyAsync ?? (static (source, destination, token) => source.CopyToAsync(destination, token));
        try
        {
            await using var source = new FileStream(
                Path.GetFullPath(sourcePath),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);
            await using var destination = new FileStream(
                snapshotPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            await copy(source, destination, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new WorkspaceBackupPackageSnapshot(snapshotRoot, snapshotPath, deleteRoot);
        }
        catch
        {
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
            // Snapshot cleanup is best effort and must not mask primary outcomes.
        }
    }

    internal static void DeleteSnapshotRoot(string path) => Directory.Delete(path, recursive: true);
}
