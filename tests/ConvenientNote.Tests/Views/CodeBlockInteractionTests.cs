using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using ConvenientNote.Services;
using ConvenientNote.Views;
using ICSharpCode.AvalonEdit;
using Xunit;

namespace ConvenientNote.Tests.Views;

public sealed class CodeBlockInteractionTests
{
    [Fact]
    public void SelectedCodeConvertsToHighlightedEditorAndKeepsWhitespaceOnSave() => Sta(() =>
    {
        var control = new RichNoteEditorControl();
        using var host = new Host(control);
        var editor = (RichTextBox)control.FindName("Editor");
        var paragraph = new Paragraph(new Run("int value = 1;\n    Console.WriteLine(value);"));
        editor.Document = new FlowDocument(paragraph);
        editor.Selection.Select(paragraph.ContentStart, paragraph.ContentEnd);
        var expected = editor.Selection.Text;
        control.InsertCodeBlock();
        var block = Assert.Single(editor.Document.Blocks.OfType<CodeBlock>());
        Assert.Equal(expected, block.CodeText);
        var view = Assert.IsType<CodeBlockControl>(block.Child);
        var codeEditor = (TextEditor)view.FindName("CodeEditor");
        Assert.Equal("C#", codeEditor.SyntaxHighlighting.Name);
        codeEditor.AppendText("\n// 中文注释");
        Assert.EndsWith("// 中文注释", block.CodeText);
        var service = new RichTextDocumentService();
        var loaded = service.Load(service.Save(editor.Document).Json, "");
        Assert.Equal(block.CodeText, Assert.Single(loaded.Blocks.OfType<CodeBlock>()).CodeText);
        ((CheckBox)view.FindName("WrapCheckBox")).IsChecked = false;
        Assert.False(block.WrapCode); Assert.False(codeEditor.WordWrap);
    });

    [Fact]
    public void FoldAndSearchRevealCodeWithoutLosingEditedText() => Sta(() =>
    {
        var control = new RichNoteEditorControl();
        using var host = new Host(control);
        var editor = (RichTextBox)control.FindName("Editor");
        var heading = new Paragraph(new Run("示例"));
        DocumentOutline.SetHeadingLevel(heading, 1);
        editor.Document = new FlowDocument(heading);
        editor.Document.Blocks.Add(new CodeBlock { CodeText = "var target = 123;" });
        editor.Document.Blocks.Add(new Paragraph(new Run("后续正文")));
        control.ToggleSection(heading);
        Assert.True(control.FindInDocument("target"));
        var block = Assert.Single(editor.Document.Blocks.OfType<CodeBlock>());
        var view = Assert.IsType<CodeBlockControl>(block.Child);
        Assert.Equal("target", ((TextEditor)view.FindName("CodeEditor")).SelectedText);
    });

    [Fact]
    public void InlineCodeKeepsSurroundingTextAndRoundTrips() => Sta(() =>
    {
        var control = new RichNoteEditorControl();
        using var host = new Host(control);
        var editor = (RichTextBox)control.FindName("Editor");
        var run = new Run("解释 List<T> 用法");
        editor.Document = new FlowDocument(new Paragraph(run));
        editor.Selection.Select(run.ContentStart.GetPositionAtOffset(3)!, run.ContentStart.GetPositionAtOffset(10)!);
        control.ApplyInlineCode();
        var saved = new RichTextDocumentService().Save(editor.Document);
        Assert.Contains("解释 List<T> 用法", saved.PlainText);
        Assert.Contains("\"inlineCode\":true", saved.Json);
        editor.Undo();
        Assert.DoesNotContain("\"inlineCode\":true", new RichTextDocumentService().Save(editor.Document).Json);
    });

    [Fact]
    public void ToolbarUndoAndRedoUseFocusedCodeHistory() => Sta(() =>
    {
        var control = new RichNoteEditorControl();
        using var host = new Host(control);
        var editor = (RichTextBox)control.FindName("Editor");
        editor.Document = new FlowDocument(new Paragraph());
        control.InsertCodeBlock();
        var block = Assert.Single(editor.Document.Blocks.OfType<CodeBlock>());
        var view = Assert.IsType<CodeBlockControl>(block.Child);
        var codeEditor = (TextEditor)view.FindName("CodeEditor");
        control.UpdateLayout();
        codeEditor.Focus();
        codeEditor.TextArea.RaiseEvent(new System.Windows.Input.KeyboardFocusChangedEventArgs(
            System.Windows.Input.Keyboard.PrimaryDevice, 0, editor, codeEditor.TextArea)
            { RoutedEvent = System.Windows.Input.Keyboard.GotKeyboardFocusEvent });
        codeEditor.AppendText("var value = 1;");
        Assert.True(codeEditor.CanUndo);
        Assert.Same(view, typeof(RichNoteEditorControl).GetField("_activeCodeView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(control));
        ((Button)control.FindName("UndoEditorButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(block.CodeText);
        Assert.Same(block, Assert.Single(editor.Document.Blocks.OfType<CodeBlock>()));
        ((Button)control.FindName("RedoEditorButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("var value = 1;", block.CodeText);
    });

    [Fact]
    public void InlineCodeToggleRestoresExistingLocalFont() => Sta(() =>
    {
        var control = new RichNoteEditorControl();
        using var host = new Host(control);
        var editor = (RichTextBox)control.FindName("Editor");
        var run = new Run("List<T>") { FontFamily = new System.Windows.Media.FontFamily("Arial") };
        var paragraph = new Paragraph(run);
        editor.Document = new FlowDocument(paragraph);
        editor.Selection.Select(run.ContentStart, run.ContentEnd);
        control.ApplyInlineCode();
        var code = Assert.Single(paragraph.Inlines.OfType<InlineCode>());
        var inner = Assert.IsType<Run>(code.Inlines.FirstInline);
        Assert.Equal("Consolas", inner.FontFamily.Source);
        editor.Selection.Select(inner.ContentStart, inner.ContentEnd);
        control.ApplyInlineCode();
        Assert.Empty(paragraph.Inlines.OfType<InlineCode>());
        Assert.Equal("Arial", Assert.IsType<Run>(paragraph.Inlines.FirstInline).FontFamily.Source);
    });

    [Fact]
    public void CodeEditorRendersWithThemeAndUsableHeight() => Sta(() =>
    {
        var control = new RichNoteEditorControl();
        control.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse("""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
              xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes">
              <ResourceDictionary.MergedDictionaries>
                <materialDesign:BundledTheme BaseTheme="Light" PrimaryColor="Indigo" SecondaryColor="Teal" />
                <ResourceDictionary Source="pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml" />
              </ResourceDictionary.MergedDictionaries>
            </ResourceDictionary>
            """));
        using var host = new Host(control);
        ((TextBox)control.FindName("TitleTextBox")).Text = "C# 随记";
        var editor = (RichTextBox)control.FindName("Editor");
        var heading = new Paragraph(new Run("LINQ 查询示例")) { FontSize = 22 };
        DocumentOutline.SetHeadingLevel(heading, 1);
        editor.Document = new FlowDocument(heading);
        var block = new CodeBlock { CodeText = "// 保存查询结果，保留缩进\nvar numbers = new List<int> { 1, 2, 3 };\nvar result = numbers.Where(x => x > 1).ToList();\n\nforeach (var value in result)\n{\n    Console.WriteLine(value);\n}" };
        editor.Document.Blocks.Add(block);
        editor.Document.Blocks.Add(new Paragraph(new Run("调用 ")));
        ((Paragraph)editor.Document.Blocks.LastBlock).Inlines.Add(new InlineCode(new Run("ToList()")));
        ((Paragraph)editor.Document.Blocks.LastBlock).Inlines.Add(new Run(" 可以保存本次查询结果。"));
        control.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        control.UpdateLayout();
        var view = Assert.IsType<CodeBlockControl>(block.Child);
        Assert.InRange(view.ActualHeight, 150, 500);
        Assert.True(view.ActualWidth > 400);
        var output = Environment.GetEnvironmentVariable("CONVENIENT_NOTE_CODE_PREVIEW");
        if (!string.IsNullOrWhiteSpace(output))
        {
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)control.ActualWidth, (int)control.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(control);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = System.IO.File.Create(output);
            encoder.Save(stream);
        }
    });

    private sealed class Host : IDisposable
    {
        private readonly Window _window;
        public Host(RichNoteEditorControl control) { _window = new Window { Content = control, Width = 1400, Height = 820, Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false }; _window.Show(); }
        public void Dispose() => _window.Close();
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
