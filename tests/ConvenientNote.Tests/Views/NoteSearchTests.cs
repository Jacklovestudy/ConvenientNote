using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using ConvenientNote.Views;
using Xunit;

namespace ConvenientNote.Tests.Views;

public sealed class NoteSearchTests
{
    [Fact]
    public void SearchKeepsTypingFocusAndEscapeClosesOnlySearch() => Sta(() =>
    {
        var control = new RichNoteEditorControl();
        var host = new Window { Content = control, Width = 1000, Height = 600, ShowActivated = false, ShowInTaskbar = false, Left = -10000 };
        host.Show();
        try
        {
            var editor = (RichTextBox)control.FindName("Editor");
            editor.Document = new FlowDocument(new Paragraph(new Run("目标内容")));
            var search = (TextBox)control.FindName("DocumentSearchBox");
            var bar = (Border)control.FindName("SearchBar");
            ((Button)control.FindName("OpenSearchButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Visibility.Visible, bar.Visibility);
            search.Text = "目标";
            Assert.Equal("目标", editor.Selection.Text);
            Assert.True(search.IsFocused);
            control.UpdateLayout();
            var preview = Environment.GetEnvironmentVariable("CONVENIENT_NOTE_SEARCH_PREVIEW");
            if (!string.IsNullOrWhiteSpace(preview))
            {
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)control.ActualWidth, (int)control.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(control);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var stream = System.IO.File.Create(preview);
                encoder.Save(stream);
            }
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(host)!, 0, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            search.RaiseEvent(escape);
            Assert.True(escape.Handled);
            Assert.Equal(Visibility.Collapsed, bar.Visibility);
            Assert.True(editor.IsFocused);
            search.Text = "新增";
            editor.Document.Blocks.Add(new Paragraph(new Run("新增内容")));
            ((Button)control.FindName("OpenSearchButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("1 / 1", ((TextBlock)control.FindName("SearchStatusText")).Text);
            Assert.Equal("新增", editor.Selection.Text);
        }
        finally { host.Close(); }
    });

    [Fact]
    public void TypingSearchSelectsMatchAndButtonsCycleBothDirections() => Sta(() =>
    {
        var control = new RichNoteEditorControl();
        var editor = (RichTextBox)control.FindName("Editor");
        editor.Document = new FlowDocument(new Paragraph(new Run("目标 a 目标 b 目标")));
        var search = (TextBox)control.FindName("DocumentSearchBox");
        search.Text = "目标";
        Assert.Equal("目标", editor.Selection.Text);
        var first = editor.Selection.Start;
        var status = Assert.IsType<TextBlock>(control.FindName("SearchStatusText"));
        Assert.Equal("1 / 3", status.Text);
        var previous = Assert.IsType<Button>(control.FindName("FindPreviousButton"));
        previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("3 / 3", status.Text);
        Assert.True(editor.Selection.Start.CompareTo(first) > 0);
        ((Button)control.FindName("FindNextButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("1 / 3", status.Text);
        Assert.Equal(0, editor.Selection.Start.CompareTo(first));
        search.Text = "不存在";
        Assert.Contains("0 / 0", status.Text);
        Assert.False(previous.IsEnabled);
        search.Text = "目标";
        Assert.Equal("1 / 3", status.Text);
        search.Clear();
        Assert.Equal("0 / 0", status.Text);
        Assert.False(previous.IsEnabled);
    });

    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
