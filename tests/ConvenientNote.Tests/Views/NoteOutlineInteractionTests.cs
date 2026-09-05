using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using ConvenientNote.Services;
using ConvenientNote.Views;
using Xunit;

namespace ConvenientNote.Tests.Views;

public sealed class NoteOutlineInteractionTests
{
    [Fact]
    public void ReopeningRestoresNestedFoldFlagsAndHiddenChapterWithoutCreatingUndoHistory()
    {
        Sta(() =>
        {
            var control = new RichNoteEditorControl();
            using var host = new TestHost(control);
            var editor = (RichTextBox)control.FindName("Editor");
            var heading = new Paragraph(new Run("章"));
            var child = new Paragraph(new Run("节"));
            var body = new Paragraph(new Run("正文标记"));
            DocumentOutline.SetHeadingLevel(heading, 1); DocumentOutline.SetIsCollapsed(heading, true);
            DocumentOutline.SetHeadingLevel(child, 2); DocumentOutline.SetIsCollapsed(child, true);
            DocumentOutline.SetHeadingLevel(body, 3);
            var document = new FlowDocument(heading);
            document.Blocks.Add(child); document.Blocks.Add(body);
            var service = new RichTextDocumentService();
            editor.Document = service.Load(service.Save(document).Json, "");
            control.RestoreSavedFolds();
            var first = (Paragraph)editor.Document.Blocks.FirstBlock;
            Assert.IsType<FoldedSection>(first.NextBlock);
            Assert.Equal(3, DocumentOutline.GetEntries(editor.Document).Count);
            Assert.False(editor.CanUndo);
            control.ToggleSection(first);
            Assert.Contains(editor.Document.Blocks.Cast<Block>(), block => block is FoldedSection);
            Assert.True(control.FindInDocument("正文标记"));
            Assert.Equal("正文标记", editor.Selection.Text);
        });
    }

    [Fact]
    public void RenderedOutlineShowsVisibleHeadingControlsAndFitsSidebar()
    {
        Sta(() =>
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
            using var host = new TestHost(control);
            ((TextBox)control.FindName("TitleTextBox")).Text = "C# 随记";
            var editor = (RichTextBox)control.FindName("Editor");
            editor.Document = new FlowDocument();
            Paragraph Heading(string title)
            {
                var p = new Paragraph(new Run(title)) { FontSize = 22 };
                DocumentOutline.SetHeadingLevel(p, 1); editor.Document.Blocks.Add(p); return p;
            }
            Heading("10 Enumerable<T> 与 IQueryable<T>");
            editor.Document.Blocks.Add(new Paragraph(new Run("前者用于枚举对象；后者可以将查询表达式交给查询提供程序。")));
            Heading("11 LINQ Select / SelectMany");
            editor.Document.Blocks.Add(new Paragraph(new Run("Select 做投影，SelectMany 将多个序列展开为一个序列。")));
            var leak = Heading("12 内存泄漏");
            foreach (var text in new[] { "静态集合不断添加对象；", "缓存没有容量或过期策略；", "事件订阅没有取消；", "Lambda 闭包捕获大对象。" })
                editor.Document.Blocks.Add(new Paragraph(new Run(text)));
            Heading("13 什么是 GC Root");
            editor.Document.Blocks.Add(new Paragraph(new Run("GC 从一组明确的起点开始寻找仍然可达的对象，这些起点称为 GC Root。")));
            control.ToggleSection(leak);
            control.UpdateLayout();
            Assert.True(((FoldedSection)leak.NextBlock!).Child.IsEnabled);
            var gutter = (Canvas)control.FindName("HeadingGutter");
            Assert.Equal(4, gutter.Children.Count);
            Assert.Contains(gutter.Children.OfType<Button>(), button => button.Visibility == Visibility.Visible);
            var sidebar = (TabControl)control.FindName("SidebarTabs");
            Assert.True(sidebar.ActualWidth > 150);
            var output = Environment.GetEnvironmentVariable("CONVENIENT_NOTE_OUTLINE_PREVIEW");
            if (!string.IsNullOrWhiteSpace(output))
            {
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)control.ActualWidth, (int)control.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(control);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var stream = System.IO.File.Create(output);
                encoder.Save(stream);
            }
            ((Button)((FoldedSection)leak.NextBlock!).Child).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsType<Paragraph>(leak.NextBlock);
        });
    }

    [Fact]
    public void NumberedConversionRequiresConfirmationAndHonorsUncheckedCandidates()
    {
        Sta(() =>
        {
            var control = new RichNoteEditorControl();
            using var host = new TestHost(control);
            var editor = (RichTextBox)control.FindName("Editor");
            editor.Document = new FlowDocument(new Paragraph(new Run("12 内存泄漏")));
            editor.Document.Blocks.Add(new Paragraph(new Run("13 什么是 GC Root")));
            ((Button)control.FindName("GenerateSectionsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(DocumentOutline.GetEntries(editor.Document));
            var candidates = (StackPanel)control.FindName("NumberedCandidatesList");
            Assert.Equal(2, candidates.Children.Count);
            ((CheckBox)candidates.Children[1]).IsChecked = false;
            ((Button)control.FindName("ConfirmSectionsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("12 内存泄漏", Assert.Single(DocumentOutline.GetEntries(editor.Document)).Title);
            editor.Undo();
            Assert.Empty(DocumentOutline.GetEntries(editor.Document));
        });
    }

    [Fact]
    public void SearchLeavesUnrelatedSectionFoldedAndSelectAllIncludesHiddenContent()
    {
        Sta(() =>
        {
            var control = new RichNoteEditorControl();
            using var host = new TestHost(control);
            var editor = (RichTextBox)control.FindName("Editor");
            var first = new Paragraph(new Run("第一章"));
            var next = new Paragraph(new Run("第二章"));
            DocumentOutline.SetHeadingLevel(first, 1); DocumentOutline.SetHeadingLevel(next, 1);
            editor.Document = new FlowDocument(first);
            editor.Document.Blocks.Add(new Paragraph(new Run("无关内容")));
            editor.Document.Blocks.Add(next);
            editor.Document.Blocks.Add(new Paragraph(new Run("目标内容")));
            control.ToggleSection(first); control.ToggleSection(next);
            Assert.True(control.FindInDocument("目标"));
            Assert.Equal("目标", editor.Selection.Text);
            Assert.IsType<FoldedSection>(first.NextBlock);
            ApplicationCommands.SelectAll.Execute(null, editor);
            Assert.Contains("无关内容", editor.Selection.Text);
            Assert.Contains("目标内容", editor.Selection.Text);
        });
    }

    [Fact]
    public void EnterAfterCollapsedHeadingExpandsBodyAndCreatesOrdinaryParagraph()
    {
        Sta(() =>
        {
            var control = new RichNoteEditorControl();
            using var host = new TestHost(control);
            var editor = (RichTextBox)control.FindName("Editor");
            var heading = new Paragraph(new Run("章节"));
            DocumentOutline.SetHeadingLevel(heading, 1);
            editor.Document = new FlowDocument(heading);
            editor.Document.Blocks.Add(new Paragraph(new Run("保留正文")));
            control.ToggleSection(heading);
            editor.CaretPosition = heading.ContentEnd;
            EditingCommands.EnterParagraphBreak.Execute(null, editor);
            Assert.DoesNotContain(editor.Document.Blocks.Cast<Block>(), b => b is FoldedSection);
            Assert.Equal(0, DocumentOutline.GetHeadingLevel(editor.CaretPosition.Paragraph!));
            Assert.Contains("保留正文", new RichTextDocumentService().Save(editor.Document).PlainText);
        });
    }

    [Fact]
    public void HeadingMetadataUndoesTogetherWithFormattingAndNavigationButtonIsRemoved()
    {
        Sta(() =>
        {
            var control = new RichNoteEditorControl();
            using var host = new TestHost(control);
            var editor = (RichTextBox)control.FindName("Editor");
            editor.Document = new FlowDocument(new Paragraph(new Run("知识点")));
            editor.CaretPosition = editor.Document.ContentStart;
            editor.IsUndoEnabled = false; editor.IsUndoEnabled = true;
            ((ComboBox)control.FindName("ParagraphStyleComboBox")).SelectedIndex = 1;
            Assert.Equal(1, DocumentOutline.GetHeadingLevel((Paragraph)editor.Document.Blocks.FirstBlock));
            editor.Undo();
            Assert.Equal(0, DocumentOutline.GetHeadingLevel((Paragraph)editor.Document.Blocks.FirstBlock));
            editor.Redo();
            Assert.Equal(1, DocumentOutline.GetHeadingLevel((Paragraph)editor.Document.Blocks.FirstBlock));
            Assert.Null(control.FindName("NavigationPointButton"));
        });
    }

    [Fact]
    public void UndoAndRedoFoldNeverLoseOrDuplicateBody()
    {
        Sta(() =>
        {
            var control = new RichNoteEditorControl();
            using var host = new TestHost(control);
            var editor = (RichTextBox)control.FindName("Editor");
            var heading = new Paragraph(new Run("章节"));
            editor.Document = new FlowDocument(heading);
            editor.Document.Blocks.Add(new Paragraph(new Run("唯一正文")));
            DocumentOutline.SetHeadingLevel(heading, 1);
            var service = new RichTextDocumentService();
            var original = service.ExtractPlainText(editor.Document);
            editor.IsUndoEnabled = false; editor.IsUndoEnabled = true;
            control.ToggleSection(heading);
            Assert.True(editor.CanUndo);
            Assert.Equal(original, service.Save(editor.Document).PlainText);
            editor.Undo();
            Assert.Equal(original, service.Save(editor.Document).PlainText);
            Assert.DoesNotContain(editor.Document.Blocks.Cast<Block>(), b => b is FoldedSection);
            editor.Redo();
            Assert.Equal(original, service.Save(editor.Document).PlainText);
            Assert.Contains(editor.Document.Blocks.Cast<Block>(), b => b is FoldedSection);
        });
    }

    [Fact]
    public void ExpandingAndUndoingExpansionKeepsNestedFoldsAndChapters()
    {
        Sta(() =>
        {
            var control = new RichNoteEditorControl();
            var editor = (RichTextBox)control.FindName("Editor");
            var heading = new Paragraph(new Run("章节"));
            var child = new Paragraph(new Run("子章节"));
            var body = new Paragraph(new Run("唯一正文"));
            DocumentOutline.SetHeadingLevel(heading, 1);
            DocumentOutline.SetHeadingLevel(child, 2);
            DocumentOutline.SetHeadingLevel(body, 3);
            editor.Document = new FlowDocument(heading);
            editor.Document.Blocks.Add(child); editor.Document.Blocks.Add(body);
            var service = new RichTextDocumentService();
            var original = service.ExtractPlainText(editor.Document);
            control.ToggleSection(child);
            control.ToggleSection(heading);
            control.ToggleSection(heading);
            Assert.Equal(original, service.Save(editor.Document).PlainText);
            Assert.Equal(3, DocumentOutline.GetEntries(editor.Document).Count);
            Assert.Contains(editor.Document.Blocks.Cast<Block>(), b => b is FoldedSection);
            editor.Undo();
            Assert.Equal(original, service.Save(editor.Document).PlainText);
            editor.Redo();
            Assert.Equal(original, service.Save(editor.Document).PlainText);
        });
    }

    [Fact]
    public void FoldSaveExpandPreservesBodyAndFollowingChapter()
    {
        Sta(() =>
        {
            var control = new RichNoteEditorControl();
            var editor = (RichTextBox)control.FindName("Editor");
            var first = new Paragraph(new Run("12 内存泄漏"));
            var body = new Paragraph(new Run("事件订阅没有取消"));
            var next = new Paragraph(new Run("13 GC Root"));
            editor.Document = new FlowDocument();
            editor.Document.Blocks.Add(first);
            editor.Document.Blocks.Add(body);
            editor.Document.Blocks.Add(next);
            DocumentOutline.SetHeadingLevel(first, 1);
            DocumentOutline.SetHeadingLevel(next, 1);
            control.ToggleSection(first);
            Assert.IsType<FoldedSection>(first.NextBlock);
            Assert.Same(next, editor.Document.Blocks.LastBlock);
            var saved = new RichTextDocumentService().Save(editor.Document);
            Assert.Contains("事件订阅没有取消", saved.PlainText);
            var loaded = new RichTextDocumentService().Load(saved.Json, "");
            Assert.True(DocumentOutline.GetIsCollapsed((Paragraph)loaded.Blocks.FirstBlock));
            control.ToggleSection(first);
            Assert.Equal("事件订阅没有取消", new TextRange(first.NextBlock!.ContentStart, first.NextBlock.ContentEnd).Text.Trim());
        });
    }

    [Fact]
    public void SearchRevealsNestedHiddenMatch()
    {
        Sta(() =>
        {
            var control = new RichNoteEditorControl();
            var editor = (RichTextBox)control.FindName("Editor");
            var heading = new Paragraph(new Run("12 内存泄漏"));
            var body = new Paragraph(new Run("事件订阅没有取消"));
            editor.Document = new FlowDocument();
            editor.Document.Blocks.Add(heading);
            editor.Document.Blocks.Add(body);
            DocumentOutline.SetHeadingLevel(heading, 1);
            control.ToggleSection(heading);
            Assert.True(control.FindInDocument("事件订阅"));
            Assert.Equal("事件订阅", editor.Selection.Text);
            Assert.DoesNotContain(editor.Document.Blocks.Cast<Block>(), b => b is FoldedSection);
        });
    }

    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private sealed class TestHost : IDisposable
    {
        private readonly Window _window;
        public TestHost(RichNoteEditorControl control)
        {
            _window = new Window { Content = control, ShowActivated = false, ShowInTaskbar = false, Left = -10000, Top = -10000, Width = 1400, Height = 820 };
            _window.Show();
        }
        public void Dispose() => _window.Close();
    }
}
