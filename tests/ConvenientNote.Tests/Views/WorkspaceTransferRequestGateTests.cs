using ConvenientNote.Views;
using Xunit;

namespace ConvenientNote.Tests.Views;

public sealed class WorkspaceTransferRequestGateTests
{
    [Fact]
    public void CompleteAllowsTheNextWorkspaceTransferRequest()
    {
        var gate = new WorkspaceTransferRequestGate();

        Assert.True(gate.TryBegin());
        Assert.False(gate.TryBegin());

        gate.Complete();

        Assert.True(gate.TryBegin());
    }

    [Fact]
    public void ActiveTransferBlocksWindowCloseEvenWithoutANotesView()
    {
        var gate = new WorkspaceTransferRequestGate();

        Assert.False(WorkspaceTransferCloseGuard.ShouldCancelWindowClose(gate));
        Assert.True(gate.TryBegin());
        Assert.True(gate.IsInProgress);
        Assert.True(WorkspaceTransferCloseGuard.ShouldCancelWindowClose(gate));

        gate.Complete();

        Assert.False(gate.IsInProgress);
        Assert.False(WorkspaceTransferCloseGuard.ShouldCancelWindowClose(gate));
    }

    [Fact]
    public async Task NotesGateDrainsExistingOperationsAndBlocksNewOnesUntilResumed()
    {
        var gate = new NotesReplacementOperationGate();
        var oldOperation = gate.TryBegin();
        Assert.NotNull(oldOperation);
        var releaseOldOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = FinishWhenReleasedAsync(oldOperation, releaseOldOperation.Task);

        var prepared = gate.PrepareAndDrainAsync();

        Assert.False(prepared.IsCompleted);
        Assert.Null(gate.TryBegin());

        releaseOldOperation.SetResult();
        await Task.WhenAll(inFlight, prepared);
        Assert.Null(gate.TryBegin());

        gate.Resume();

        using var nextOperation = gate.TryBegin();
        Assert.NotNull(nextOperation);
    }

    private static async Task FinishWhenReleasedAsync(IDisposable operation, Task release)
    {
        using (operation)
        {
            await release;
        }
    }
}
