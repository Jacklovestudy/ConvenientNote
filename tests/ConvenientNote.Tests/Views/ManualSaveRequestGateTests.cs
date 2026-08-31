using ConvenientNote.Views;
using Xunit;

namespace ConvenientNote.Tests.Views;

public sealed class ManualSaveRequestGateTests
{
    [Fact]
    public void InFlightSaveCoalescesNewRequestsUntilCompletion()
    {
        var gate = new ManualSaveRequestGate();

        Assert.True(gate.TryBegin(isRepeat: false));
        Assert.False(gate.TryBegin(isRepeat: false));

        gate.Complete();

        Assert.True(gate.TryBegin(isRepeat: false));
    }

    [Fact]
    public void AutoRepeatedKeyDoesNotStartSave()
    {
        var gate = new ManualSaveRequestGate();

        Assert.False(gate.TryBegin(isRepeat: true));
        Assert.True(gate.TryBegin(isRepeat: false));
    }
}
