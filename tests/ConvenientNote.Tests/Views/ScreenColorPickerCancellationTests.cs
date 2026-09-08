using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ConvenientNote.ColorPicker.Application;
using ConvenientNote.ColorPicker.UI;
using Xunit;

namespace ConvenientNote.Tests.Views;

public sealed class ScreenColorPickerCancellationTests
{
    [Theory]
    [InlineData("Escape")]
    [InlineData("Close")]
    [InlineData("Confirm")]
    public void FinishingModalPickerReturnsAndAllowsAnotherPick(string action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var owner = new Window { Left = -20000, Top = -20000, Width = 100, Height = 100, ShowInTaskbar = false };
                owner.Show();
                try
                {
                    var capture = new FakeCapture();
                    for (var attempt = 0; attempt < 2; attempt++)
                    {
                        var picker = new ScreenColorPickerWindow(capture.Capture(), capture) { Owner = owner };
                        Exception? cancelFailure = null;
                        picker.Loaded += (_, _) => picker.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (action == "Escape")
                                    picker.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(picker)!, 0, Key.Escape)
                                    { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                                else if (action == "Confirm")
                                    picker.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                                    { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                                else picker.Close();
                            }
                            catch (Exception e) { cancelFailure = e; picker.Hide(); }
                        }), DispatcherPriority.ApplicationIdle);
                        Assert.Equal(action == "Confirm", picker.ShowDialog() == true);
                        Assert.Null(cancelFailure);
                        Assert.Equal(action == "Confirm", picker.SelectedColor.HasValue);
                        Assert.False(picker.IsVisible);
                        Assert.True(owner.IsEnabled);
                        Assert.True(IsWindowEnabled(new WindowInteropHelper(owner).Handle));
                    }
                }
                finally { owner.Close(); }
            }
            catch (Exception e) { failure = e; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Cancelling the picker left its modal dispatcher running.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(nint window);

    private sealed class FakeCapture : IScreenCapture
    {
        public ScreenSnapshot Capture() => new(-20000, -20000, 32, 32, new byte[32 * 32 * 4]);
        public (int X, int Y) GetCursorPosition() => (-20000, -20000);
    }
}
