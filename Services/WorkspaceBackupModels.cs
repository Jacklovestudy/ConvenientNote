using ConvenientNote.Domain.Workspaces;

namespace ConvenientNote.Services;

public sealed record WorkspaceBackupManifest(
    string Format,
    int SchemaVersion,
    string AppVersion,
    DateTimeOffset ExportedAtUtc);

public sealed record WorkspaceBackupDocument(
    Guid WorkspaceId,
    string WorkspaceName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<WorkspaceBackupNote> Notes);

public sealed record WorkspaceBackupNote(
    Guid Id,
    string BoardKey,
    string Priority,
    string Title,
    string Content,
    double X,
    double Y,
    double Width,
    double Height,
    string Color,
    int ZIndex,
    bool IsCompleted,
    string RichContent,
    Guid? NotebookId,
    IReadOnlyList<string> Tags,
    bool IsPinned,
    bool IsFavorite,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record WorkspaceBackupPreview(
    string WorkspaceName,
    int NoteCount,
    DateTimeOffset ExportedAtUtc);

public sealed record WorkspaceBackupExportResult(
    string PackagePath,
    int NoteCount);

public sealed record WorkspaceBackupImportResult(
    WorkspaceId WorkspaceId,
    string WorkspaceName,
    int NoteCount);
