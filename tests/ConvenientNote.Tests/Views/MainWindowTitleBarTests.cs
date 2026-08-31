using System.Threading;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;

namespace ConvenientNote.Tests.Views;

public sealed class MainWindowTitleBarTests
{
    [Fact]
    public void WindowControlStyles_HaveMatchingDimensionsAndHoverFeedback()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = System.Windows.Application.Current ?? new System.Windows.Application();
                var resources = new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/ConvenientNote;component/Resources/WindowControls.xaml",
                        UriKind.Absolute)
                };
                var standardStyle = Assert.IsType<Style>(resources["TitleBarButtonStyle"]);
                var closeStyle = Assert.IsType<Style>(resources["TitleBarCloseButtonStyle"]);
                var minimizeButton = new Button { Style = standardStyle, Content = "_" };
                var maximizeButton = new Button { Style = standardStyle, Content = "□" };
                var closeButton = new Button { Style = closeStyle, Content = "×" };

                Assert.Equal(46, minimizeButton.Width);
                Assert.Equal(minimizeButton.Width, maximizeButton.Width);
                Assert.Equal(minimizeButton.Width, closeButton.Width);
                Assert.Equal(48, minimizeButton.Height);
                Assert.Equal(minimizeButton.Height, maximizeButton.Height);
                Assert.Equal(minimizeButton.Height, closeButton.Height);

                minimizeButton.Measure(new Size(46, 48));
                minimizeButton.Arrange(new Rect(0, 0, 46, 48));
                minimizeButton.ApplyTemplate();
                var hoverBackground = Assert.IsType<Border>(
                    minimizeButton.Template.FindName("PART_HoverBackground", minimizeButton));
                var icon = Assert.IsType<ContentPresenter>(
                    minimizeButton.Template.FindName("PART_Icon", minimizeButton));
                var iconScale = Assert.IsType<ScaleTransform>(icon.RenderTransform);

                minimizeButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
                {
                    RoutedEvent = Mouse.MouseEnterEvent
                });

                WaitForAnimations(TimeSpan.FromMilliseconds(250));
                iconScale = Assert.IsType<ScaleTransform>(icon.RenderTransform);
                Assert.True(hoverBackground.Opacity > 0.95);
                Assert.True(iconScale.ScaleX > 1.05);
                Assert.True(iconScale.ScaleY > 1.05);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void WaitForAnimations(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = duration
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
