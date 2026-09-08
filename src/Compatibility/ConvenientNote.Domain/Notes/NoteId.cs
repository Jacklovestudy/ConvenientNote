namespace ConvenientNote.Domain.Notes;

public readonly record struct NoteId(Guid Value)
{
    public static NoteId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
