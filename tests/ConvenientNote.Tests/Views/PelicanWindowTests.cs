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
    public void FrontPoseHasTwoEyesAndNoLongSidewaysBill() => Sta(() =>
    {
        var artwork = new PelicanVisual { FrontFacing = 1 };
        artwork.Measure(new Size(360, 310)); artwork.Arrange(new Rect(0, 0, 360, 310));
        var pixels = Pixels(artwork, PetAction.Ride, 0);
        foreach (var eyeX in new[] { 194, 222 })
        {
            var index = (43 * 360 + eyeX) * 4;
            Assert.Equal(255, pixels[index + 3]);
            Assert.True(pixels[index + 2] < 100, "Both front-facing eyes must be visible.");
        }
        Assert.Equal(0, pixels[(70 * 360 + 300) * 4 + 3]);
    });

    [Fact]
    public void HoverFacesTheViewerWithoutTurningTheBicycle() => Sta(() =>
    {
        var window = new PelicanWindow(new PetPreferences(), () => { }, () => { }, () => { }) { Opacity = 0 };
        try
        {
            window.Show(); window.UpdateLayout();
            var scale = window.Artwork.ActualWidth / 360;
            new RenderTargetBitmap(360, 310, 96, 96, PixelFormats.Pbgra32).Render(window.Artwork);
            for (var i = 0; i < 15; i++) window.UpdatePointerInteraction(.1, new Point(140 * scale, 150 * scale));
            Assert.True(window.Artwork.FrontFacing > .9);
            Assert.True(window.Artwork.BubbleOpacity > .9);
            Assert.Equal(1, window.Motion.Direction);
            for (var i = 0; i < 15; i++) window.UpdatePointerInteraction(.1, null);
            Assert.True(window.Artwork.FrontFacing < .1);
            Assert.True(window.Artwork.BubbleOpacity < .1);
            Assert.Equal(1, window.Motion.Direction);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void CollisionFramesStayInsideWindowInBothDirections() => Sta(() =>
    {
        var artwork = new PelicanVisual();
        artwork.Measure(new Size(360, 310)); artwork.Arrange(new Rect(0, 0, 360, 310));
        foreach (var direction in new[] { 1, -1 })
        for (var frame = 0; frame < 30; frame++)
        {
            artwork.Update(PetAction.Crash, .7, frame / 10d, direction); artwork.UpdateLayout();
            new RenderTargetBitmap(360, 310, 96, 96, PixelFormats.Pbgra32).Render(artwork);
            var bounds = VisualTreeHelper.GetDescendantBounds(artwork);
            Assert.True(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= 360 && bounds.Bottom <= 310,
                $"Collision frame {frame}, direction {direction}: {bounds}");
        }
    });

    [Fact]
    public void HoverTracksScaledAndMirroredHeadAndDoesNotOverrideBoost() => Sta(() =>
    {
        var window = new PelicanWindow(new PetPreferences(), () => { }, () => { }, () => { }) { Opacity = 0 };
        try
        {
            window.Show();
            foreach (var size in new[] { .6, 1.6 })
            foreach (var direction in new[] { 1, -1 })
            {
                window.ResizePet(size); window.UpdateLayout();
                window.Artwork.Update(PetAction.Ride, 0, 0, direction);
                window.Artwork.UpdateLayout();
                var bitmap = new RenderTargetBitmap(500, 500, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window.Artwork);
                var scale = window.Artwork.ActualWidth / 360;
                Point At(double x, double y) => new((direction < 0 ? 360 - x : x) * scale, y * scale);
                Assert.True(window.Artwork.ContactAt(At(207, 43)).OnHead);
                Assert.False(window.Artwork.ContactAt(At(78, 245)).OnPet);
                Assert.True(window.UpdatePointerInteraction(.1, At(207, 43)));
                window.UpdatePointerInteraction(.1, At(225, 43));
                window.UpdatePointerInteraction(.1, At(207, 43));
                Assert.True(window.Artwork.IsPetting);
                Assert.False(window.UpdatePointerInteraction(.1, null));
                window.Motion.Boost();
                Assert.False(window.UpdatePointerInteraction(.1, At(207, 43)));
                Assert.Equal(PetAction.Boost, window.Motion.Action);
                window.Motion.Ride();
            }
            window.Artwork.Update(PetAction.Sleep, 0, 0, 1); window.Artwork.UpdateLayout();
            new RenderTargetBitmap(500, 500, 96, 96, PixelFormats.Pbgra32).Render(window.Artwork);
            window.Motion.Sleep();
            var head = new RotateTransform(48, 200, 99).Transform(new Point(207, 43));
            head = new Point(head.X * window.Artwork.ActualWidth / 360, head.Y * window.Artwork.ActualHeight / 310);
            Assert.True(window.Artwork.ContactAt(head).OnHead);
            window.UpdatePointerInteraction(.4, head);
            Assert.Equal(PetAction.Sleep, window.Motion.Action);
            window.UpdatePointerInteraction(.4, head);
            Assert.Equal(PetAction.React, window.Motion.Action);
        }
        finally { window.Close(); }
    });

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
