using ConvenientNote.Views;
using Xunit;

namespace ConvenientNote.Tests.Views;

public sealed class DeferredWindowCloseCoordinatorTests
{
    [Fact]
    public void CompletingFlushSchedulesCloseWithoutCallingItInline()
    {
        var coordinator = new DeferredWindowCloseCoordinator();
        var scheduled = new List<Action>();
        var closeCalls = 0;

        Assert.True(coordinator.TryBeginFlush());
        Assert.False(coordinator.TryBeginFlush());

        coordinator.CompleteFlush(true, scheduled.Add, () => closeCalls++);

        Assert.True(coordinator.CanClose);
        Assert.Equal(0, closeCalls);
        Assert.Single(scheduled)();
        Assert.Equal(1, closeCalls);
    }

    [Fact]
    public void FailedFlushKeepsWindowOpenAndAllowsRetry()
    {
        var coordinator = new DeferredWindowCloseCoordinator();
        var scheduled = new List<Action>();

        Assert.True(coordinator.TryBeginFlush());

        coordinator.CompleteFlush(false, scheduled.Add, () => { });

        Assert.False(coordinator.CanClose);
        Assert.Empty(scheduled);
        Assert.True(coordinator.TryBeginFlush());
    }
}
