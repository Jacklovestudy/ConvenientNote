namespace ConvenientNote.Views;

internal interface IWorkspaceReplacementParticipant
{
    Task PrepareForWorkspaceReplacementAsync();

    void ResumeAfterWorkspaceReplacementFailure();
}

internal sealed class WorkspaceReplacementOperationGate
{
    private readonly object _syncRoot = new();
    private int _activeOperationCount;
    private bool _isPreparing;
    private TaskCompletionSource? _drained;

    public bool IsPreparing
    {
        get
        {
            lock (_syncRoot)
            {
                return _isPreparing;
            }
        }
    }

    public IDisposable? TryBegin()
    {
        lock (_syncRoot)
        {
            if (_isPreparing)
            {
                return null;
            }

            _activeOperationCount++;
            return new Operation(this);
        }
    }

    public Task PrepareAndDrainAsync()
    {
        lock (_syncRoot)
        {
            _isPreparing = true;
            if (_activeOperationCount == 0)
            {
                return Task.CompletedTask;
            }

            _drained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _drained.Task;
        }
    }

    public void CancelPreparation()
    {
        lock (_syncRoot)
        {
            if (_activeOperationCount != 0)
            {
                throw new InvalidOperationException("Cannot resume workspace mutations while operations are still running.");
            }

            _isPreparing = false;
            _drained = null;
        }
    }

    private void CompleteOperation()
    {
        lock (_syncRoot)
        {
            _activeOperationCount--;
            if (_isPreparing && _activeOperationCount == 0)
            {
                _drained?.TrySetResult();
            }
        }
    }

    private sealed class Operation(WorkspaceReplacementOperationGate owner) : IDisposable
    {
        private WorkspaceReplacementOperationGate? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.CompleteOperation();
    }
}
