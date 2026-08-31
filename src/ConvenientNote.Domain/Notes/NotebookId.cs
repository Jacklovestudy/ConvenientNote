namespace ConvenientNote.Domain.Notes;

public readonly record struct NotebookId(Guid Value)
{
    public static NotebookId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}
