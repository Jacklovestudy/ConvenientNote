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
        Assert.True(gate.IsRequestInProgress);
        Assert.True(WorkspaceTransferCloseGuard.ShouldCancelWindowClose(gate));

        gate.Complete();

        Assert.False(gate.IsRequestInProgress);
        Assert.False(WorkspaceTransferCloseGuard.ShouldCancelWindowClose(gate));
    }

    [Fact]
    public async Task PrepareAndDrainBlocksNewOperationsUntilAnExistingOperationFinishes()
    {
        var gate = new WorkspaceReplacementOperationGate();
        var oldOperation = gate.TryBegin();
        Assert.NotNull(oldOperation);
        var releaseOldOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = FinishWhenReleasedAsync(oldOperation, releaseOldOperation.Task);

        var prepared = gate.PrepareAndDrainAsync();

        Assert.False(prepared.IsCompleted);
        Assert.Null(gate.TryBegin());

        releaseOldOperation.SetResult();
        await Task.WhenAll(inFlight, prepared);
    }

    [Fact]
    public async Task CoordinatorDrainsBeforeDisablingAndKeepsTheBlockThroughViewRemoval()
    {
        var events = new List<string>();
        var participant = new BlockingParticipant(events);
        var coordinator = new WorkspaceReplacementCoordinator();

        var replacement = coordinator.ExecuteAsync(
            [participant],
            () => events.Add("disable"),
            () =>
            {
                events.Add("import");
                return Task.FromResult("imported");
            },
            () => events.Add("remove"),
            () =>
            {
                events.Add("reload");
                return Task.CompletedTask;
            },
            () => events.Add("navigate"),
            () => events.Add("enable"));

        Assert.Equal(["prepare"], events);
        participant.Release();

        Assert.Equal("imported", await replacement);
        Assert.Equal(
            ["prepare", "disable", "import", "remove", "reload", "navigate", "enable"],
            events);
    }

    [Fact]
    public async Task CoordinatorResumesOldParticipantsWhenImportFailsBeforeViewsAreRemoved()
    {
        var events = new List<string>();
        var participant = new RecoverableParticipant(events);
        var coordinator = new WorkspaceReplacementCoordinator();

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ExecuteAsync(
            [participant],
            () => events.Add("disable"),
            () => Task.FromException<string>(new InvalidOperationException("import failed")),
            () => events.Add("remove"),
            () => Task.CompletedTask,
            () => events.Add("navigate"),
            () => events.Add("enable")));

        Assert.Equal(["prepare", "disable", "resume", "enable"], events);
        using var newOperation = participant.Gate.TryBegin();
        Assert.NotNull(newOperation);
    }

    [Fact]
    public async Task CoordinatorDoesNotResumeRemovedParticipantsWhenRefreshFails()
    {
        var events = new List<string>();
        var participant = new RecoverableParticipant(events);
        var coordinator = new WorkspaceReplacementCoordinator();

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ExecuteAsync(
            [participant],
            () => events.Add("disable"),
            () => Task.FromResult("imported"),
            () => events.Add("remove"),
            () => Task.FromException(new InvalidOperationException("reload failed")),
            () => events.Add("navigate"),
            () => events.Add("enable")));

        Assert.Equal(["prepare", "disable", "remove", "enable"], events);
        Assert.Null(participant.Gate.TryBegin());
    }

    [Fact]
    public async Task CoordinatorDoesNotResumeParticipantsWhenRemovalFailsAfterImportReturns()
    {
        var events = new List<string>();
        var participant = new RecoverableParticipant(events);
        var coordinator = new WorkspaceReplacementCoordinator();

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ExecuteAsync(
            [participant],
            () => events.Add("disable"),
            () => Task.FromResult("imported"),
            () =>
            {
                events.Add("remove");
                throw new InvalidOperationException("remove failed");
            },
            () => Task.CompletedTask,
            () => events.Add("navigate"),
            () => events.Add("enable")));

        Assert.Equal(["prepare", "disable", "remove", "enable"], events);
        Assert.Null(participant.Gate.TryBegin());
    }

    private static async Task FinishWhenReleasedAsync(IDisposable operation, Task release)
    {
        using (operation)
        {
            await release;
        }
    }

    private sealed class BlockingParticipant(List<string> events) : IWorkspaceReplacementParticipant
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task PrepareForWorkspaceReplacementAsync()
        {
            events.Add("prepare");
            await _release.Task;
        }

        public void Release() => _release.SetResult();

        public void ResumeAfterWorkspaceReplacementFailure()
        {
        }
    }

    private sealed class RecoverableParticipant(List<string> events) : IWorkspaceReplacementParticipant
    {
        public WorkspaceReplacementOperationGate Gate { get; } = new();

        public Task PrepareForWorkspaceReplacementAsync()
        {
            events.Add("prepare");
            return Gate.PrepareAndDrainAsync();
        }

        public void ResumeAfterWorkspaceReplacementFailure()
        {
            events.Add("resume");
            Gate.CancelPreparation();
        }
    }
}
