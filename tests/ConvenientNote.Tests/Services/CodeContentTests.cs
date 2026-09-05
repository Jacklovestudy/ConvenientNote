using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows;
using ConvenientNote.Services;
using Xunit;

namespace ConvenientNote.Tests.Services;

public sealed class CodeContentTests
{
    [Fact]
    public void CodeJsonPreservesWhitespaceAndMetadata() => RunSta(() =>
    {
        var service = new RichTextDocumentService();
        var document = service.Load("""{"version":1,"blocks":[{"kind":"code","codeText":"  var x = 1;\n\t\n","codeLanguage":"C#","wrapCode":false}]}""", "fallback");
        var saved = service.Save(document);
        Assert.Equal("  var x = 1;\n\t\n", saved.PlainText);
        Assert.Contains("\"kind\":\"code\"", saved.Json);
        Assert.Contains("\"wrapCode\":false", saved.Json);
        Assert.Equal(saved.Json, service.Save(service.Load(saved.Json, "")).Json);
    });

    [Fact]
    public void InlineCodeRoundTripsWithDecorationsAndSize() => RunSta(() =>
    {
        var service = new RichTextDocumentService();
        var document = service.Load("""{"version":1,"blocks":[{"inlines":[{"text":"x < 3","inlineCode":true,"bold":true,"underline":true,"strikethrough":true,"fontSize":19}]}]}""", "");
        var saved = service.Save(document);
        Assert.Contains("\"inlineCode\":true", saved.Json);
        Assert.Contains("\"fontSize\":19", saved.Json);
        Assert.Contains("\"underline\":true", saved.Json);
        Assert.Contains("\"strikethrough\":true", saved.Json);
    });

    [Fact]
    public void NestedAndFoldedCodeClonesExcludeEditorAndPreserveFormatting() => RunSta(() =>
    {
        var service = new RichTextDocumentService();
        var code = new CodeBlock { CodeText = "\treturn 42;\n\n", WrapCode = false, Child = new UnserializableEditor() };
        var item = new ListItem { Padding = new Thickness(7) };
        item.Blocks.Add(code);
        var list = new List(item) { StartIndex = 4, MarkerStyle = TextMarkerStyle.Decimal };
        var section = new Section(list) { FontSize = 21, Padding = new Thickness(9) };
        var document = new FlowDocument(section);
        var cloned = service.CloneBlocks(document.Blocks);
        var clonedSection = Assert.IsType<Section>(cloned.Blocks.FirstBlock);
        var clonedList = Assert.IsType<List>(clonedSection.Blocks.FirstBlock);
        var clonedCode = Assert.IsType<CodeBlock>(clonedList.ListItems.FirstListItem.Blocks.FirstBlock);
        Assert.Null(clonedCode.Child);
        Assert.IsType<UnserializableEditor>(code.Child);
        Assert.Equal(4, clonedList.StartIndex);
        Assert.Equal(new Thickness(7), clonedList.ListItems.FirstListItem.Padding);
        Assert.Equal(new Thickness(9), clonedSection.Padding);
        Assert.Equal(code.CodeText, clonedCode.CodeText);
        Assert.False(clonedCode.WrapCode);
        Assert.Equal(service.Save(document).PlainText, service.Save(cloned).PlainText);
        Assert.Contains(code.CodeText, service.ExtractPlainText(document));
        var nextItem = new ListItem(new Paragraph(new Run("next item")));
        list.ListItems.Add(nextItem);
        document.Blocks.Add(new Paragraph(new Run("after list")));
        var continuedText = service.ExtractPlainText(document);
        Assert.Contains("next item", continuedText);
        Assert.Contains("after list", continuedText);
        Assert.Equal(continuedText, service.ExtractPlainText(service.CloneBlocks(document.Blocks)));
        var heading = new Paragraph(new Run("Heading"));
        var folded = new FlowDocument(heading);
        folded.Blocks.Add(new FoldedSection(heading, cloned) { Child = new TextBlock { Text = "Hidden UI label" } });
        var saved = service.Save(folded);
        Assert.Contains(code.CodeText, saved.PlainText);
        Assert.DoesNotContain("Hidden UI label", saved.PlainText);
        Assert.Contains(code.CodeText, service.Save(service.Load(saved.Json, "")).PlainText);
    });

    public sealed class UnserializableEditor : TextBox
    {
        public string Failure
        {
            get => throw new InvalidOperationException("Live editor must not be serialized.");
            set { }
        }
    }

    [Fact]
    public void CodeDefaultsAndUndoRestorePersistedDependencyProperties() => RunSta(() =>
    {
        var defaults = new CodeBlock();
        Assert.Equal(string.Empty, defaults.CodeText);
        Assert.Equal("C#", defaults.CodeLanguage);
        Assert.True(defaults.WrapCode);
        Assert.Null(defaults.Child);
        var code = new CodeBlock { CodeText = "  x();\n", CodeLanguage = "C#", WrapCode = false };
        var document = new FlowDocument(new Paragraph(new Run("before")));
        document.Blocks.Add(code);
        var editor = new RichTextBox { Document = document };
        var window = new Window { Content = editor, Width = 400, Height = 300, Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false };
        window.Show();
        try
        {
            editor.BeginChange();
            document.Blocks.Remove(code);
            editor.EndChange();
            Assert.True(editor.CanUndo);
            editor.Undo();
            var restored = Assert.IsType<CodeBlock>(document.Blocks.LastBlock);
            Assert.Equal("  x();\n", restored.CodeText);
            Assert.Equal("C#", restored.CodeLanguage);
            Assert.False(restored.WrapCode);
        }
        finally { window.Close(); }
    });

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
