namespace ConvenientNote.Views;

internal sealed class WorkspaceReplacementCoordinator
{
    public async Task<T> ExecuteAsync<T>(
        IEnumerable<IWorkspaceReplacementParticipant> participants,
        Action disableMainContent,
        Func<Task<T>> importWorkspaceAsync,
        Action removeCachedViews,
        Func<Task> reloadWorkspaceIdentityAsync,
        Action reloadActiveNavigation,
        Action enableMainContent)
    {
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(disableMainContent);
        ArgumentNullException.ThrowIfNull(importWorkspaceAsync);
        ArgumentNullException.ThrowIfNull(removeCachedViews);
        ArgumentNullException.ThrowIfNull(reloadWorkspaceIdentityAsync);
        ArgumentNullException.ThrowIfNull(reloadActiveNavigation);
        ArgumentNullException.ThrowIfNull(enableMainContent);

        var replacementCommitted = false;
        var participantList = participants.ToList();
        try
        {
            await Task.WhenAll(participantList.Select(participant => participant.PrepareForWorkspaceReplacementAsync()));
            disableMainContent();
            var result = await importWorkspaceAsync();
            replacementCommitted = true;
            removeCachedViews();
            await reloadWorkspaceIdentityAsync();
            reloadActiveNavigation();
            return result;
        }
        catch
        {
            if (!replacementCommitted)
            {
                foreach (var participant in participantList)
                {
                    participant.ResumeAfterWorkspaceReplacementFailure();
                }
            }

            throw;
        }
        finally
        {
            enableMainContent();
        }
    }
}
