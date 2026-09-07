namespace ConvenientNote.Views;

public sealed class WorkspaceTransferRequestGate
{
    private readonly object _syncRoot = new();
    private bool _requestInProgress;

    public bool IsInProgress
    {
        get
        {
            lock (_syncRoot)
            {
                return _requestInProgress;
            }
        }
    }

    public bool TryBegin()
    {
        lock (_syncRoot)
        {
            if (_requestInProgress)
            {
                return false;
            }

            _requestInProgress = true;
            return true;
        }
    }

    public void Complete()
    {
        lock (_syncRoot)
        {
            _requestInProgress = false;
        }
    }
}

internal static class WorkspaceTransferCloseGuard
{
    public static bool ShouldCancelWindowClose(WorkspaceTransferRequestGate gate) =>
        gate.IsInProgress;
}
