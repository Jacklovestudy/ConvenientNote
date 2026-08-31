namespace ConvenientNote.Views;

internal sealed class DeferredWindowCloseCoordinator
{
    private bool _flushInProgress;

    public bool CanClose { get; private set; }

    public bool TryBeginFlush()
    {
        if (_flushInProgress)
        {
            return false;
        }

        _flushInProgress = true;
        return true;
    }

    public void CompleteFlush(bool succeeded, Action<Action> scheduleClose, Action close)
    {
        ArgumentNullException.ThrowIfNull(scheduleClose);
        ArgumentNullException.ThrowIfNull(close);

        _flushInProgress = false;
        if (!succeeded)
        {
            return;
        }

        CanClose = true;
        scheduleClose(close);
    }
}
