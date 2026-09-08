namespace ConvenientNote.Infrastructure.Persistence.Entities;

public sealed class WorkspaceEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<NoteEntity> Notes { get; set; } = new();
}
