using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConvenientNote.DesktopPet.Domain;
using ConvenientNote.DesktopPet.UI;
using Xunit;

namespace ConvenientNote.Tests.Views;

public sealed class PelicanWindowTests
{
    [Fact]
    public void ArticulatedFramesHaveTransparentBackgroundAndChangeWithPedaling() => Sta(() =>
    {
        var artwork = new PelicanVisual();
        artwork.Measure(new Size(360, 310)); artwork.Arrange(new Rect(0, 0, 360, 310));
        var first = Pixels(artwork, PetAction.Ride, 0);
        var next = Pixels(artwork, PetAction.Ride, 1.2);
        Assert.Equal(0, first[3]);
        Assert.False(first.SequenceEqual(next));
        foreach (var action in Enum.GetValues<PetAction>()) Assert.Contains(Pixels(artwork, action, 1), value => value != 0);
    });

    [Fact]
    public void PetWindowDoesNotActivateAndRecoversOffscreenPosition() => Sta(() =>
    {
        var window = new PelicanWindow(new PetPreferences(Left: -99999, Top: -99999), () => { }, () => { }, () => { }) { Opacity = 0 };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.False(window.ShowActivated);
            Assert.False(window.ShowInTaskbar);
            var style = GetWindowLong(new WindowInteropHelper(window).Handle, -20);
            Assert.NotEqual(0, style & 0x08000000);
            Assert.True(window.Left >= SystemParameters.VirtualScreenLeft);
            Assert.True(window.Top >= SystemParameters.VirtualScreenTop);
            window.ResizePet(.6);
            Assert.Equal(156, window.Width, 3);
            window.UpdateLayout();
            Assert.True(window.Artwork.ActualWidth <= window.ActualWidth + 1,
                $"Artwork width {window.Artwork.ActualWidth} exceeds window {window.ActualWidth}");
            Assert.True(window.Artwork.ActualHeight <= window.ActualHeight + 1,
                $"Artwork height {window.Artwork.ActualHeight} exceeds window {window.ActualHeight}");
        }
        finally { window.Close(); }
    });

    private static byte[] Pixels(PelicanVisual artwork, PetAction action, double phase)
    {
        artwork.Update(action, phase, .25, 1); artwork.UpdateLayout();
        var bitmap = new RenderTargetBitmap(360, 310, 96, 96, PixelFormats.Pbgra32); bitmap.Render(artwork);
        var bytes = new byte[360 * 310 * 4]; bitmap.CopyPixels(bytes, 360 * 4, 0); return bytes;
    }

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
}
