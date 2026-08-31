using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using ConvenientNote.Views;
using Xunit;

namespace ConvenientNote.Tests.Views;

public sealed class RichNoteEditorFontSizeTests
{
    [Fact]
    public void ParagraphStyleChangeRefreshesDisplayedFontSize()
    {
        RunSta(() =>
        {
            var control = new RichNoteEditorControl();
            var host = CreateHost(control);

            host.Show();
            try
            {
                var editor = Assert.IsType<RichTextBox>(control.FindName("Editor"));
                var paragraphStyles = Assert.IsType<ComboBox>(control.FindName("ParagraphStyleComboBox"));
                var fontSizes = Assert.IsType<ComboBox>(control.FindName("FontSizeComboBox"));
                var paragraph = new Paragraph(new Run("标题"));
                editor.Document.Blocks.Clear();
                editor.Document.Blocks.Add(paragraph);
                editor.Selection.Select(paragraph.ContentStart, paragraph.ContentEnd);

                paragraphStyles.SelectedIndex = 1;

                var selectedSize = Assert.IsType<ComboBoxItem>(fontSizes.SelectedItem);
                Assert.Equal("28", selectedSize.Tag);
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Fact]
    public void UndoingFontSizeChangeRefreshesDisplayedFontSize()
    {
        RunSta(() =>
        {
            var control = new RichNoteEditorControl();
            var host = CreateHost(control);

            host.Show();
            try
            {
                var editor = Assert.IsType<RichTextBox>(control.FindName("Editor"));
                var paragraphStyles = Assert.IsType<ComboBox>(control.FindName("ParagraphStyleComboBox"));
                var fontSizes = Assert.IsType<ComboBox>(control.FindName("FontSizeComboBox"));
                var paragraph = new Paragraph(new Run("标题"));
                editor.Document.Blocks.Clear();
                editor.Document.Blocks.Add(paragraph);
                editor.Selection.Select(paragraph.ContentStart, paragraph.ContentEnd);

                paragraphStyles.SelectedIndex = 1;
                ApplicationCommands.Undo.Execute(null, editor);

                var selectedSize = Assert.IsType<ComboBoxItem>(fontSizes.SelectedItem);
                Assert.Equal("15", selectedSize.Tag);
            }
            finally
            {
                host.Close();
            }
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
