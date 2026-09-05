using System.Windows.Controls;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using ConvenientNote.Services;
using Xunit;

namespace ConvenientNote.Tests.Services;

public sealed class DocumentOutlineTests
{
    [Fact]
    public void LegacyTopLevelNavigationBecomesChapterWithoutChangingTextOrFormatting() => RunSta(() =>
    {
        var service = new RichTextDocumentService();
        var document = service.Load("""{"version":1,"blocks":[{"isNavigationPoint":true,"fontSize":15,"inlines":[{"kind":"text","text":"2 ref、out、in","bold":true}]},{"inlines":[{"kind":"text","text":"说明文字"}]},{"headingLevel":2,"isNavigationPoint":true,"inlines":[{"kind":"text","text":"现有二级标题"}]}]}""", "");
        var first = (Paragraph)document.Blocks.FirstBlock;
        Assert.Equal(1, DocumentOutline.GetHeadingLevel(first));
        Assert.False(DocumentOutline.GetIsNavigationPoint(first));
        Assert.Equal(15, first.FontSize);
        Assert.Equal(new[] { 1, 2 }, DocumentOutline.GetEntries(document).Select(entry => entry.Level));
        Assert.Contains("说明文字", service.Save(document).PlainText);
        Assert.Equal(1, DocumentOutline.GetHeadingLevel((Paragraph)service.Load(service.Save(document).Json, "").Blocks.FirstBlock));
    });

    [Fact]
    public void EntriesShowOnlyChaptersAndIgnoreLegacyListBookmarks() => RunSta(() =>
    {
        var heading = new Paragraph(new Run("第一章"));
        var bookmark = new Paragraph(new Run("关键位置"));
        var ignored = new Paragraph(new Run("普通文本"));
        DocumentOutline.SetHeadingLevel(heading, 1);
        DocumentOutline.SetIsNavigationPoint(heading, true);
        DocumentOutline.SetIsCollapsed(heading, true);
        DocumentOutline.SetIsNavigationPoint(bookmark, true);
        var document = new FlowDocument(heading);
        var nested = new List(new ListItem(bookmark));
        var item = new ListItem(ignored);
        item.Blocks.Add(nested);
        document.Blocks.Add(new List(item));

        Assert.Collection(DocumentOutline.GetEntries(document),
            entry => { Assert.Same(heading, entry.Paragraph); Assert.Equal(1, entry.Level); Assert.Equal("第一章", entry.Title); Assert.True(entry.IsHeading); Assert.True(entry.IsCollapsed); });
    });

    [Fact]
    public void SectionStopsAtSameOrHigherHeadingAndKeepsListsAndLowerHeadings() => RunSta(() =>
    {
        var first = new Paragraph(new Run("章"));
        var child = new Paragraph(new Run("节"));
        var list = new List(new ListItem(new Paragraph(new Run("内容"))));
        var next = new Paragraph(new Run("下一章"));
        DocumentOutline.SetHeadingLevel(first, 1);
        DocumentOutline.SetHeadingLevel(child, 2);
        DocumentOutline.SetHeadingLevel(next, 1);
        var document = new FlowDocument(first);
        document.Blocks.Add(child); document.Blocks.Add(list); document.Blocks.Add(next);

        Assert.Equal(new Block[] { child, list }, DocumentOutline.GetSectionBlocks(document, first));
        Assert.Equal(new Block[] { list }, DocumentOutline.GetSectionBlocks(document, child));
        Assert.Empty(DocumentOutline.GetSectionBlocks(document, next));
        Assert.Empty(DocumentOutline.GetSectionBlocks(document, new Paragraph()));
    });

    [Fact]
    public void NumberedCandidatesAcceptParagraphPrefixesAndIgnoreCodeAndListItems() => RunSta(() =>
    {
        var document = new FlowDocument();
        var candidates = new[] { "1 第一章", "2. 第二章", "3、第三章", "  4.第四章" }.Select(text => new Paragraph(new Run(text))).ToArray();
        foreach (var paragraph in candidates) document.Blocks.Add(paragraph);
        foreach (var text in new[] { "普通文本", "5.", "12.5", "6. var x = 1;", "7. foo();", "8. // comment" }) document.Blocks.Add(new Paragraph(new Run(text)));
        document.Blocks.Add(new List(new ListItem(new Paragraph(new Run("9 列表项")))));

        Assert.Equal(candidates, DocumentOutline.FindNumberedCandidates(document));
    });

    [Fact]
    public void RoundTripPreservesOutlineAndNestedListMediaWithoutLosingBody() => RunSta(() =>
    {
        var heading = new Paragraph(new Run("章节"));
        DocumentOutline.SetHeadingLevel(heading, 2);
        DocumentOutline.SetIsCollapsed(heading, true);
        var body = new Paragraph(new Bold(new Run("完整正文")));
        var bookmark = new Paragraph(new Run("内层标记"));
        bookmark.Inlines.Add(new InlineUIContainer(new Image { Tag = "note/image.png", Width = 123 }));
        DocumentOutline.SetIsNavigationPoint(bookmark, true);
        var item = new ListItem(new Paragraph(new Run("外层")));
        item.Blocks.Add(new List(new ListItem(bookmark)));
        var document = new FlowDocument(heading);
        document.Blocks.Add(body); document.Blocks.Add(new List(item));
        var service = new RichTextDocumentService();

        var saved = service.Save(document);
        var loaded = service.Load(saved.Json, "fallback");
        var entries = DocumentOutline.GetEntries(loaded);

        Assert.Contains("\"version\":1", saved.Json);
        Assert.Single(entries);
        Assert.Equal(2, entries[0].Level);
        Assert.True(entries[0].IsCollapsed);
        Assert.Contains("完整正文", service.ExtractPlainText(loaded));
        Assert.Contains("内层标记", service.ExtractPlainText(loaded));
        Assert.Contains("note/image.png", saved.MediaPaths);
        var outer = Assert.IsType<List>(loaded.Blocks.LastBlock);
        var inner = Assert.IsType<List>(outer.ListItems.FirstListItem.Blocks.LastBlock);
        var loadedBookmark = Assert.IsType<Paragraph>(inner.ListItems.FirstListItem.Blocks.FirstBlock);
        Assert.True(DocumentOutline.GetIsNavigationPoint(loadedBookmark));
        var image = Assert.IsType<Image>(Assert.Single(loadedBookmark.Inlines.OfType<InlineUIContainer>()).Child);
        Assert.Equal(123, image.Width);
        Assert.Contains("note/image.png", service.Save(loaded).MediaPaths);
    });

    [Fact]
    public void FoldedSnapshotSavesExpandedBodyAndClonesNeverReparentOriginals() => RunSta(() =>
    {
        var heading = new Paragraph(new Run("第一章"));
        var child = new Paragraph(new Run("子章节"));
        DocumentOutline.SetHeadingLevel(heading, 1);
        DocumentOutline.SetIsCollapsed(heading, true);
        DocumentOutline.SetHeadingLevel(child, 2);
        var original = new FlowDocument(child);
        original.Blocks.Add(new Paragraph(new Run("隐藏正文")));
        var service = new RichTextDocumentService();
        var snapshot = service.CloneBlocks(original.Blocks);
        var document = new FlowDocument(heading);
        document.Blocks.Add(new FoldedSection(heading, snapshot));

        var saved = service.Save(document);
        var loaded = service.Load(saved.Json, "fallback");

        Assert.Same(original, child.Parent);
        Assert.Equal(2, DocumentOutline.GetEntries(document).Count);
        Assert.Equal(2, DocumentOutline.GetSectionBlocks(document, heading).Count);
        Assert.Equal("第一章\r\n子章节\r\n隐藏正文", saved.PlainText);
        Assert.Equal(3, loaded.Blocks.Count);
        Assert.DoesNotContain(loaded.Blocks, block => block is FoldedSection);
        Assert.True(DocumentOutline.GetIsCollapsed(Assert.IsType<Paragraph>(loaded.Blocks.FirstBlock)));
    });

    [Fact]
    public void FoldingClonePreservesFormattingTablesAndImageSource() => RunSta(() =>
    {
        var decorations = new TextDecorationCollection();
        decorations.Add(TextDecorations.Underline[0]);
        decorations.Add(TextDecorations.Strikethrough[0]);
        var run = new Run("双重装饰") { TextDecorations = decorations, FontFamily = new FontFamily("Consolas") };
        var paragraph = new Paragraph(run) { Margin = new Thickness(3, 5, 7, 11) };
        DocumentOutline.SetHeadingLevel(paragraph, 3);
        DocumentOutline.SetIsNavigationPoint(paragraph, true);
        paragraph.Inlines.Add(new InlineUIContainer(new Image
        {
            Width = 71,
            Tag = "note/picture.png",
            Source = new DrawingImage(new GeometryDrawing(Brushes.Red, null, new RectangleGeometry(new Rect(0, 0, 8, 8))))
        }));
        var table = new Table();
        var rowGroup = new TableRowGroup();
        var row = new TableRow();
        var cell = new TableCell(new Paragraph(new Run("表格内容")));
        row.Cells.Add(cell); rowGroup.Rows.Add(row); table.RowGroups.Add(rowGroup);
        var document = new FlowDocument(paragraph) { FontFamily = new FontFamily("Courier New") };
        document.Blocks.Add(table);

        var clone = new RichTextDocumentService().CloneBlocks(document.Blocks);

        Assert.Equal(2, clone.Blocks.Count);
        Assert.Same(document, paragraph.Parent);
        var clonedParagraph = Assert.IsType<Paragraph>(clone.Blocks.FirstBlock);
        Assert.Equal(new Thickness(3, 5, 7, 11), clonedParagraph.Margin);
        Assert.Equal("Courier New", clonedParagraph.FontFamily.Source);
        var clonedRun = Assert.IsType<Run>(clonedParagraph.Inlines.FirstInline);
        Assert.Equal("Consolas", clonedRun.FontFamily.Source);
        Assert.Equal(2, clonedRun.TextDecorations.Count);
        Assert.Equal(3, DocumentOutline.GetHeadingLevel(clonedParagraph));
        Assert.True(DocumentOutline.GetIsNavigationPoint(clonedParagraph));
        var clonedImage = Assert.IsType<Image>(Assert.Single(clonedParagraph.Inlines.OfType<InlineUIContainer>()).Child);
        Assert.Equal("note/picture.png", clonedImage.Tag);
        Assert.Equal(71, clonedImage.Width);
        Assert.NotNull(clonedImage.Source);
        var clonedTable = Assert.IsType<Table>(clone.Blocks.LastBlock);
        Assert.Contains("表格内容", new TextRange(clonedTable.ContentStart, clonedTable.ContentEnd).Text);
    });

    [Fact]
    public void FoldedPlainTextMatchesOriginalWithListsEmptyParagraphsAndImages() => RunSta(() =>
    {
        var heading = new Paragraph(new Run("章"));
        var first = new Paragraph(new Run("正文"));
        first.Inlines.Add(new LineBreak());
        first.Inlines.Add(new LineBreak());
        var list = new List { MarkerStyle = TextMarkerStyle.Decimal, StartIndex = 4 };
        var item = new ListItem(new Paragraph(new Run("列表项")));
        item.Blocks.Add(new Paragraph());
        item.Blocks.Add(new List(new ListItem(new Paragraph(new Run("嵌套项")))));
        list.ListItems.Add(item);
        var image = new Paragraph(new InlineUIContainer(new Image { Tag = "note/image.png" }));
        var document = new FlowDocument(heading);
        document.Blocks.Add(first); document.Blocks.Add(new Paragraph()); document.Blocks.Add(list);
        document.Blocks.Add(image); document.Blocks.Add(new Paragraph(new Run("末尾")));
        var service = new RichTextDocumentService();
        var expected = service.ExtractPlainText(document);
        var hidden = service.CloneBlocks(new Block[] { first, document.Blocks.ElementAt(2), list, image });
        foreach (var block in new Block[] { first, document.Blocks.ElementAt(2), list, image }) document.Blocks.Remove(block);
        document.Blocks.InsertAfter(heading, new FoldedSection(heading, hidden));

        Assert.Equal(expected, service.ExtractPlainText(document));
    });

    [Fact]
    public void ClonedTextPreservesSimultaneousDecorationsWhenSavedAndReloaded() => RunSta(() =>
    {
        var decorations = new TextDecorationCollection();
        decorations.Add(TextDecorations.Underline[0]);
        decorations.Add(TextDecorations.Strikethrough[0]);
        var document = new FlowDocument(new Paragraph(new Run("保留两种装饰") { TextDecorations = decorations }));
        var service = new RichTextDocumentService();

        var loaded = service.Load(service.Save(service.CloneBlocks(document.Blocks)).Json, string.Empty);

        var paragraph = Assert.IsType<Paragraph>(Assert.Single(loaded.Blocks));
        var run = FindRun(paragraph.Inlines);
        var locations = new HashSet<TextDecorationLocation>();
        for (Inline? inline = run; inline is not null; inline = inline.Parent as Inline)
            foreach (var decoration in inline.TextDecorations) locations.Add(decoration.Location);
        Assert.Contains(TextDecorationLocation.Underline, locations);
        Assert.Contains(TextDecorationLocation.Strikethrough, locations);
    });

    private static Run FindRun(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run) return run;
            if (inline is Span span) return FindRun(span.Inlines);
        }
        throw new InvalidOperationException("Expected a text run.");
    }

    [Fact]
    public void LegacyVersionOneDefaultsToUnmarkedAndInvalidHeadingMetadataDoesNotLoseText() => RunSta(() =>
    {
        var service = new RichTextDocumentService();
        var old = service.Load("""{"version":1,"blocks":[{"kind":"paragraph","inlines":[{"kind":"text","text":"旧笔记"}]}]}""", "fallback");
        Assert.Empty(DocumentOutline.GetEntries(old));
        Assert.Equal("旧笔记", service.ExtractPlainText(old));
        var invalid = service.Load("""{"version":1,"blocks":[{"headingLevel":99,"isCollapsed":true,"inlines":[{"kind":"text","text":"保留"}]}]}""", "fallback");
        Assert.Equal("保留", service.ExtractPlainText(invalid));
        Assert.Empty(DocumentOutline.GetEntries(invalid));
    });

    private static void RunSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() => { try { action(); } catch (Exception current) { exception = current; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (exception is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
    }
}
