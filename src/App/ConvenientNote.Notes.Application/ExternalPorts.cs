using ConvenientNote.Notes.Domain.Notes;
namespace ConvenientNote.Services;

public interface INoteMediaService
{
    string MediaRoot
    {
        get;
    }
    Task<string> ImportAsync(NoteId noteId, string sourcePath, CancellationToken cancellationToken = default);
    Task DeleteOrphansAsync(NoteId noteId, IReadOnlySet<string> referencedPaths);
    string GetAbsolutePath(string relativePath);
    Task DeleteAllAsync(NoteId noteId);
}
public interface INotesBackupService
{
    Task<NotesBackupExportResult> ExportAsync(string destinationPath, CancellationToken cancellationToken = default);
    Task<NotesBackupPreview> InspectAsync(string packagePath, CancellationToken cancellationToken = default);
    Task<NotesBackupImportResult> ImportOverwriteAsync(string packagePath, CancellationToken cancellationToken = default);
    Task<NotesBackupImportResult> ImportOverwriteAsync(string packagePath, Action replacementCommitted, CancellationToken cancellationToken = default);
}
public interface INotesBackupPackageSnapshot : IDisposable, IAsyncDisposable
{
    string PackagePath
    {
        get;
    }
}
public interface INotesBackupPackageStager
{
    Task<INotesBackupPackageSnapshot> StageAsync(string sourcePath, CancellationToken cancellationToken = default);
}
public sealed class UnsupportedNotesBackupSchemaException(int schemaVersion) : Exception($"Notes backup schema version {schemaVersion} is newer than this application supports.")
{
    public int SchemaVersion { get; } = schemaVersion;
}
