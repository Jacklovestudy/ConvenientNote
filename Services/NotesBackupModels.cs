namespace ConvenientNote.Services;

public sealed record NotesBackupManifest(
    string Format,
    int SchemaVersion,
    string AppVersion,
    DateTimeOffset ExportedAtUtc);

public sealed record NotesBackupDocument(IReadOnlyList<NotesBackupNote> Notes);

public sealed record NotesBackupNote(
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

public sealed record NotesBackupPreview(int NoteCount, DateTimeOffset ExportedAtUtc);

public sealed record NotesBackupExportResult(string PackagePath, int NoteCount);

public sealed record NotesBackupImportResult(int NoteCount);
