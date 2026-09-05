using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using ConvenientNote.Services;
using ConvenientNote.Views;
using Xunit;

namespace ConvenientNote.Tests.Views;

public sealed class NoteZoomTests
{
    [Fact]
    public void ZoomedHeadingGutterStaysAlignedAndFoldingStillWorks() => Sta(() =>
    {
        var control = new RichNoteEditorControl();
        var window = new Window { Content = control, Width = 1400, Height = 820, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            var editor = (RichTextBox)control.FindName("Editor");
            var heading = new Paragraph(new Run("可缩放章节"));
            DocumentOutline.SetHeadingLevel(heading, 1);
            editor.Document = new FlowDocument(heading);
            for (var i = 0; i < 40; i++) editor.Document.Blocks.Add(new Paragraph(new Run($"正文 {i}")));
            control.ToggleSection(heading); control.ToggleSection(heading);
            var viewport = (Grid)control.FindName("EditorZoomViewport");
            control.ApplyZoomWheel(600, ModifierKeys.Control, new Point(100, 40));
            control.UpdateLayout();
            Assert.Equal(150, control.EditorZoomPercent);
            var gutter = (Canvas)control.FindName("HeadingGutter");
            var button = Assert.Single(gutter.Children.OfType<Button>());
            var rect = heading.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            var textTop = editor.TranslatePoint(new Point(0, rect.Top), viewport).Y;
            var arrowTop = gutter.TranslatePoint(new Point(0, Canvas.GetTop(button)), viewport).Y;
            Assert.InRange(Math.Abs(textTop - arrowTop), 0, 1);
            Assert.True(editor.ExtentHeight > editor.ViewportHeight);
            control.ToggleSection(heading);
            Assert.IsType<FoldedSection>(heading.NextBlock);
            Assert.Contains("正文 39", new RichTextDocumentService().Save(editor.Document).PlainText);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void ControlWheelScalesViewWithoutChangingSavedFormattingOrUndoHistory() => Sta(() =>
    {
        var control = new RichNoteEditorControl();
        var editor = (RichTextBox)control.FindName("Editor");
        editor.Document = new FlowDocument(new Paragraph(new Run("保持字号") { FontSize = 22 }));
        var service = new RichTextDocumentService();
        var before = service.Save(editor.Document).Json;
        Assert.True(control.ApplyZoomWheel(120, ModifierKeys.Control, new Point(80, 80)));
        Assert.Equal(110, control.EditorZoomPercent);
        Assert.Equal(before, service.Save(editor.Document).Json);
        Assert.False(editor.CanUndo);
        Assert.True(control.ApplyZoomWheel(-120, ModifierKeys.Control, new Point(80, 80)));
        Assert.Equal(100, control.EditorZoomPercent);
    });

    [Fact]
    public void PlainWheelDoesNotZoomAndZoomHasBoundsAndSupportsSmallDeltas() => Sta(() =>
    {
        var control = new RichNoteEditorControl();
        Assert.False(control.ApplyZoomWheel(120, ModifierKeys.None, new Point()));
        Assert.Equal(100, control.EditorZoomPercent);
        control.ApplyZoomWheel(60, ModifierKeys.Control, new Point());
        Assert.Equal(100, control.EditorZoomPercent);
        control.ApplyZoomWheel(60, ModifierKeys.Control, new Point());
        Assert.Equal(110, control.EditorZoomPercent);
        control.ApplyZoomWheel(12000, ModifierKeys.Control, new Point());
        Assert.Equal(200, control.EditorZoomPercent);
        control.ApplyZoomWheel(-12000, ModifierKeys.Control, new Point());
        Assert.Equal(50, control.EditorZoomPercent);
        ((Button)control.FindName("ResetZoomButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(100, control.EditorZoomPercent);
    });

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
