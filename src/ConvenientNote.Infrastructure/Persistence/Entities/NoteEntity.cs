namespace ConvenientNote.Infrastructure.Persistence.Entities;

public sealed class NoteEntity
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public string BoardKey { get; set; } = "day-todo";

    public string Priority { get; set; } = "blue";

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string RichContent { get; set; } = string.Empty;

    public Guid? NotebookId { get; set; }

    public string TagsJson { get; set; } = "[]";

    public bool IsPinned { get; set; }

    public bool IsFavorite { get; set; }

    public bool IsDeleted { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public string Color { get; set; } = "#FFF8B8";

    public int ZIndex { get; set; }

    public bool IsCompleted { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public WorkspaceEntity? Workspace { get; set; }
}
