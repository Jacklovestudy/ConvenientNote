namespace ConvenientNote.Platform.Contracts;

public sealed record WorkspaceInfo(Guid Id, string Name);

/// <summary>Workspace identity only. Business records belong to their modules.</summary>
public interface IWorkspaceContext
{
    Task<WorkspaceInfo> GetCurrentAsync(CancellationToken cancellationToken = default);
}
