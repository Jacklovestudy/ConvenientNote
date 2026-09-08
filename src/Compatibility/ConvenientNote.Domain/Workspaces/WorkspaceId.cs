namespace ConvenientNote.Domain.Workspaces;

public readonly record struct WorkspaceId(Guid Value)
{
    public static WorkspaceId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
