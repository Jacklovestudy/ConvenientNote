using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using ConvenientNote.Views;
using Xunit;

namespace ConvenientNote.Tests.Views;

public sealed class RichNoteEditorSaveFeedbackTests
{
    [Theory]
    [InlineData(true, "已保存")]
    [InlineData(false, "保存失败，请重试")]
    public void ManualSaveFeedbackShowsResult(bool saved, string expectedMessage)
    {
        RunSta(() =>
        {
            var control = new RichNoteEditorControl();

            control.ShowSaveFeedback(saved);

            var feedback = Assert.IsType<Border>(control.FindName("SaveFeedbackBorder"));
            var message = Assert.IsType<TextBlock>(control.FindName("SaveFeedbackText"));
            Assert.Equal(Visibility.Visible, feedback.Visibility);
            Assert.Equal(1, feedback.Opacity);
            Assert.Equal(expectedMessage, message.Text);
            Assert.Equal(expectedMessage, AutomationProperties.GetName(message));
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(message));
        });
    }

    [Fact]
    public void ManualSaveFeedbackHidesAfterDelay()
    {
        RunSta(() =>
        {
            var control = new RichNoteEditorControl();
            var host = CreateHost(control);
            var feedback = Assert.IsType<Border>(control.FindName("SaveFeedbackBorder"));

            host.Show();
            try
            {
                control.ShowSaveFeedback(true);
                WaitForDispatcher(TimeSpan.FromMilliseconds(1800));

                Assert.Equal(Visibility.Collapsed, feedback.Visibility);
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Fact]
    public void SaveFeedbackIsAnchoredAtTopOfEditor()
    {
        RunSta(() =>
        {
            var control = new RichNoteEditorControl();
            var feedback = Assert.IsType<Border>(control.FindName("SaveFeedbackBorder"));

            Assert.Equal(VerticalAlignment.Top, feedback.VerticalAlignment);
            Assert.True(feedback.Margin.Top > 0);
            Assert.Equal(0, feedback.Margin.Bottom);
        });
    }

    [Fact]
    public void ReturnButtonIsAvailableInHeaderActions()
    {
        RunSta(() =>
        {
            var control = new RichNoteEditorControl();
            var actions = Assert.IsType<StackPanel>(control.FindName("HeaderActionsPanel"));
            var button = Assert.IsType<Button>(control.FindName("HeaderBackButton"));

            Assert.Same(actions, button.Parent);
            Assert.Equal(0, actions.Children.IndexOf(button));
            Assert.Equal("返回笔记墙", AutomationProperties.GetName(button));
        });
    }

    [Fact]
    public void WordCountIsShownInBottomStatusBar()
    {
        RunSta(() =>
        {
            var control = new RichNoteEditorControl();
            var statusBar = Assert.IsType<Border>(control.FindName("EditorStatusBar"));
            var wordCount = Assert.IsType<TextBlock>(control.FindName("WordCountText"));

            Assert.Equal(3, Grid.GetRow(statusBar));
            Assert.Equal("字数：0", wordCount.Text);
        });
    }

    [Fact]
    public void EditorContentStartsAtTopLeft()
    {
        RunSta(() =>
        {
            var control = new RichNoteEditorControl();
            var editor = Assert.IsType<RichTextBox>(control.FindName("Editor"));

            Assert.Equal(VerticalAlignment.Top, editor.VerticalContentAlignment);
            Assert.Equal(HorizontalAlignment.Stretch, editor.HorizontalContentAlignment);
        });
    }

    [Fact]
    public void LineSpacingCanBeSelectedOrTypedInToolbar()
    {
        RunSta(() =>
        {
            var control = new RichNoteEditorControl();
            var lineSpacing = Assert.IsType<ComboBox>(control.FindName("LineSpacingComboBox"));

            Assert.True(lineSpacing.IsEditable);
            Assert.Contains(lineSpacing.Items.OfType<ComboBoxItem>(), item => Equals(item.Tag, "1.5"));
            Assert.Equal("行距", AutomationProperties.GetName(lineSpacing));
        });
    }

    private static Window CreateHost(RichNoteEditorControl control)
    {
        return new Window
        {
            Content = control,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Width = 1,
            Height = 1,
            Left = -10000,
            Top = -10000
        };
    }

    private static void WaitForDispatcher(TimeSpan duration)
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

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
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
}
