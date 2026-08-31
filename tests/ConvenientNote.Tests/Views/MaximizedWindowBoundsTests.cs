using Xunit;

namespace ConvenientNote.Tests.Views;

public sealed class MaximizedWindowBoundsTests
{
    [Fact]
    public void CalculateUsesMonitorWorkAreaInsteadOfFullScreen()
    {
        var monitor = new NativeRectangle(1920, 0, 3840, 1080);
        var workArea = new NativeRectangle(1920, 0, 3840, 1040);

        var bounds = MaximizedWindowBounds.Calculate(monitor, workArea);

        Assert.Equal(0, bounds.X);
        Assert.Equal(0, bounds.Y);
        Assert.Equal(1920, bounds.Width);
        Assert.Equal(1040, bounds.Height);
    }
}
