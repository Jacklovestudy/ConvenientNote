using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ConvenientNote.Services;

public sealed record RichTextSaveResult(string Json, string PlainText, IReadOnlySet<string> MediaPaths);

public sealed class RichTextDocumentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string? _mediaRoot;

    public RichTextDocumentService(string? mediaRoot = null)
    {
        _mediaRoot = mediaRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConvenientNote",
            "Media");
    }

    public RichTextSaveResult Save(FlowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var model = new DocumentModel
        {
            Blocks = DocumentOutline.LogicalBlocks(document.Blocks).Select(SerializeBlock).Where(static block => block is not null).Cast<BlockModel>().ToList()
        };
        var mediaPaths = model.Blocks
            .SelectMany(EnumerateInlines)
            .Where(static inline => inline.Kind == "image" && !string.IsNullOrWhiteSpace(inline.Text))
            .Select(static inline => inline.Text)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new RichTextSaveResult(
            JsonSerializer.Serialize(model, JsonOptions),
            ExtractPlainText(document),
            mediaPaths);
    }

    public FlowDocument Load(string? json, string fallbackPlainText)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return CreatePlainDocument(fallbackPlainText);
        }

        try
        {
            var model = JsonSerializer.Deserialize<DocumentModel>(json, JsonOptions);
            if (model?.Version != 1)
            {
                return CreatePlainDocument(fallbackPlainText);
            }

            var document = new FlowDocument();
            foreach (var block in model.Blocks)
            {
                document.Blocks.Add(DeserializeBlock(block));
            }

            // Older notes used a separate bookmark flag. Promote only standalone
            // paragraphs: list items must not silently become section boundaries.
            foreach (var paragraph in document.Blocks.OfType<Paragraph>().Where(DocumentOutline.GetIsNavigationPoint))
            {
                if (DocumentOutline.GetHeadingLevel(paragraph) == 0)
                    DocumentOutline.SetHeadingLevel(paragraph, 1);
                DocumentOutline.SetIsNavigationPoint(paragraph, false);
            }

            if (document.Blocks.Count == 0)
            {
                document.Blocks.Add(new Paragraph());
            }

            return document;
        }
        catch (JsonException)
        {
            return CreatePlainDocument(fallbackPlainText);
        }
        catch (InvalidOperationException)
        {
            return CreatePlainDocument(fallbackPlainText);
        }
    }

    public string ExtractPlainText(FlowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var text = new StringBuilder();
        var preservedEnd = 0;
        AppendPlainText(document, text, ref preservedEnd);
        while (text.Length > preservedEnd && text[^1] is '\r' or '\n') text.Length--;
        return text.ToString();
    }

    private static void AppendPlainText(FlowDocument document, StringBuilder text, ref int preservedEnd)
    {
        // Read contiguous visible ranges together: ending individual ranges at a list
        // can duplicate its generated marker, and trimming each block loses blank lines.
        var start = document.ContentStart;
        foreach (var special in EnumerateEmbeddedBlocks(document.Blocks))
        {
            text.Append(new TextRange(start, special.ContentStart).Text);
            if (special is FoldedSection folded)
                AppendPlainText(folded.HiddenDocument, text, ref preservedEnd);
            else if (special is CodeBlock code)
            {
                text.Append(code.CodeText);
                preservedEnd = text.Length;
                text.Append("\r\n");
            }
            start = FollowingContentStart(special, document);
        }
        // WPF can emit a list marker even for an empty range at ContentEnd when
        // the final list item contains an unhydrated BlockUIContainer.
        if (start.CompareTo(document.ContentEnd) < 0)
            text.Append(new TextRange(start, document.ContentEnd).Text);
    }

    private static TextPointer FollowingContentStart(Block block, FlowDocument document)
    {
        // A range beginning at a last list child's ElementEnd can synthesize the
        // list marker again when the child has no editor. Resume beyond completed
        // containers so presentation hydration never affects the stored text.
        DependencyObject? current = block;
        while (current is FrameworkContentElement element && current != document)
        {
            if (current is Block { NextBlock: { } next }) return next.ContentStart;
            if (current is ListItem { NextListItem: { } nextItem }) return nextItem.ContentStart;
            current = element.Parent;
        }
        return document.ContentEnd;
    }

    private static IEnumerable<Block> EnumerateEmbeddedBlocks(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            if (block is CodeBlock or FoldedSection) yield return block;
            else if (block is Section section)
                foreach (var child in EnumerateEmbeddedBlocks(section.Blocks)) yield return child;
            else if (block is List list)
                foreach (var item in list.ListItems)
                    foreach (var child in EnumerateEmbeddedBlocks(item.Blocks)) yield return child;
            else if (block is Table table)
                foreach (var group in table.RowGroups)
                    foreach (var row in group.Rows)
                        foreach (var cell in row.Cells)
                            foreach (var child in EnumerateEmbeddedBlocks(cell.Blocks)) yield return child;
        }
    }

    public FlowDocument CloneBlocks(IEnumerable<Block> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        var document = new FlowDocument();
        foreach (var block in blocks)
        {
            if (block is FoldedSection folded)
            {
                foreach (var hidden in DocumentOutline.LogicalBlocks(folded.HiddenDocument.Blocks))
                    document.Blocks.Add(ClonePresentationBlock(hidden));
            }
            else document.Blocks.Add(ClonePresentationBlock(block));
        }
        return document;
    }

    // Presentation snapshots preserve WPF formatting and blocks beyond the persisted JSON schema.
    // The XAML is produced only from our in-memory document, never accepted from external input.
    private static Block ClonePresentationBlock(Block block)
    {
        Block clone;
        if (block is CodeBlock code)
            clone = CopyLocalFormatting(code, new CodeBlock());
        else if (block is Section section && EnumerateEmbeddedBlocks(section.Blocks).Any())
        {
            var sectionClone = CopyLocalFormatting(section, new Section());
            foreach (var child in DocumentOutline.LogicalBlocks(section.Blocks))
                sectionClone.Blocks.Add(ClonePresentationBlock(child));
            clone = sectionClone;
        }
        else if (block is List list && list.ListItems.Any(item => EnumerateEmbeddedBlocks(item.Blocks).Any()))
        {
            var listClone = CopyLocalFormatting(list, new List());
            foreach (var item in list.ListItems)
            {
                var itemClone = CopyLocalFormatting(item, new ListItem());
                foreach (var child in DocumentOutline.LogicalBlocks(item.Blocks))
                    itemClone.Blocks.Add(ClonePresentationBlock(child));
                listClone.ListItems.Add(itemClone);
            }
            clone = listClone;
        }
        else clone = (Block)XamlReader.Parse(XamlWriter.Save(block));
        // XamlWriter saves local values; freeze the effective inherited formatting at
        // the snapshot root so it does not adopt the temporary FlowDocument defaults.
        clone.FontFamily = block.FontFamily;
        clone.FontSize = block.FontSize;
        clone.FontStretch = block.FontStretch;
        clone.FontStyle = block.FontStyle;
        clone.FontWeight = block.FontWeight;
        clone.Foreground = block.Foreground.CloneCurrentValue();
        clone.FlowDirection = block.FlowDirection;
        clone.TextAlignment = block.TextAlignment;
        clone.LineHeight = block.LineHeight;
        clone.LineStackingStrategy = block.LineStackingStrategy;
        clone.Language = block.Language;
        return clone;
    }

    private static T CopyLocalFormatting<T>(T source, T target) where T : TextElement
    {
        var values = source.GetLocalValueEnumerator();
        while (values.MoveNext())
        {
            var property = values.Current.Property;
            if (property.ReadOnly || property.Name == "Child") continue;
            var value = source.GetValue(property);
            target.SetValue(property, value is Freezable freezable ? freezable.CloneCurrentValue() : value);
        }
        target.Resources = source.Resources;
        return target;
    }

    private static IEnumerable<InlineModel> EnumerateInlines(BlockModel block) =>
        block.Inlines.Concat(block.Items.SelectMany(EnumerateInlines)).Concat(block.Children.SelectMany(EnumerateInlines));

    private static BlockModel? SerializeBlock(Block block)
    {
        if (block is CodeBlock code)
            return new BlockModel { Kind = "code", CodeText = code.CodeText, CodeLanguage = code.CodeLanguage, WrapCode = code.WrapCode };
        if (block is Paragraph paragraph)
        {
            return new BlockModel
            {
                Kind = "paragraph",
                Alignment = paragraph.TextAlignment.ToString(),
                FontSize = paragraph.FontSize,
                LineSpacing = ParagraphLineSpacing.GetRatio(paragraph),
                HeadingLevel = DocumentOutline.GetHeadingLevel(paragraph),
                IsNavigationPoint = DocumentOutline.GetIsNavigationPoint(paragraph),
                IsCollapsed = DocumentOutline.GetIsCollapsed(paragraph),
                Inlines = paragraph.Inlines.SelectMany(SerializeInline).ToList()
            };
        }

        if (block is List list)
        {
            return new BlockModel
            {
                Kind = list.MarkerStyle == TextMarkerStyle.Decimal ? "numbered-list" : "bullet-list",
                Items = list.ListItems
                    .SelectMany(static item => item.Blocks.OfType<Paragraph>())
                    .Select(SerializeBlock)
                    .Where(static item => item is not null)
                    .Cast<BlockModel>()
                    .ToList(),
                Children = list.ListItems
                    .Select(static item => new BlockModel
                    {
                        Kind = "list-item",
                        Children = DocumentOutline.LogicalBlocks(item.Blocks).Select(SerializeBlock)
                            .Where(static child => child is not null).Cast<BlockModel>().ToList()
                    })
                    .ToList(),
                Inlines = list.ListItems
                    .SelectMany(static item => item.Blocks.OfType<Paragraph>())
                    .SelectMany(static paragraph => paragraph.Inlines.SelectMany(SerializeInline).Append(new InlineModel { Kind = "line-break" }))
                    .ToList()
            };
        }

        if (block is Section section)
        {
            return new BlockModel
            {
                Kind = "section",
                Children = DocumentOutline.LogicalBlocks(section.Blocks).Select(SerializeBlock)
                    .Where(static child => child is not null).Cast<BlockModel>().ToList()
            };
        }

        return null;
    }

    private static IEnumerable<InlineModel> SerializeInline(Inline inline)
    {
        var underline = inline.TextDecorations.Any(static decoration => decoration.Location == TextDecorationLocation.Underline);
        var strikethrough = inline.TextDecorations.Any(static decoration => decoration.Location == TextDecorationLocation.Strikethrough);
        foreach (var model in SerializeInlineContent(inline))
        {
            yield return model with
            {
                Underline = model.Underline || underline,
                Strikethrough = model.Strikethrough || strikethrough,
                InlineCode = model.InlineCode || inline is InlineCode
            };
        }
    }

    private static IEnumerable<InlineModel> SerializeInlineContent(Inline inline)
    {
        switch (inline)
        {
            case Run run:
                yield return new InlineModel
                {
                    Kind = "text",
                    Text = run.Text,
                    Bold = run.FontWeight == FontWeights.Bold,
                    Italic = run.FontStyle == FontStyles.Italic,
                    Foreground = (run.Foreground as SolidColorBrush)?.Color.ToString(),
                    FontSize = run.FontSize
                };
                break;
            case Bold bold:
                foreach (var child in bold.Inlines.SelectMany(SerializeInline))
                {
                    yield return child with { Bold = true };
                }
                break;
            case Italic italic:
                foreach (var child in italic.Inlines.SelectMany(SerializeInline))
                {
                    yield return child with { Italic = true };
                }
                break;
            case Underline underline:
                foreach (var child in underline.Inlines.SelectMany(SerializeInline))
                {
                    yield return child with { Underline = true };
                }
                break;
            case LineBreak:
                yield return new InlineModel { Kind = "line-break" };
                break;
            case InlineUIContainer { Child: Image image } when image.Tag is string path:
                yield return new InlineModel { Kind = "image", Text = path, Width = image.Width };
                break;
            case Span span:
                foreach (var child in span.Inlines.SelectMany(SerializeInline))
                {
                    yield return child;
                }
                break;
        }
    }

    private Block DeserializeBlock(BlockModel model)
    {
        if (model.Kind == "code")
            return new CodeBlock { CodeText = model.CodeText ?? string.Empty, CodeLanguage = model.CodeLanguage ?? "C#", WrapCode = model.WrapCode };
        if (model.Kind == "section")
        {
            var section = new Section();
            foreach (var child in model.Children) section.Blocks.Add(DeserializeBlock(child));
            return section;
        }
        if (model.Kind is "bullet-list" or "numbered-list")
        {
            var list = new List
            {
                MarkerStyle = model.Kind == "numbered-list" ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc
            };
            if (model.Children.Count > 0)
            {
                foreach (var item in model.Children)
                {
                    var listItem = new ListItem();
                    foreach (var child in item.Children) listItem.Blocks.Add(DeserializeBlock(child));
                    list.ListItems.Add(listItem);
                }
                return list;
            }
            if (model.Items.Count > 0)
            {
                foreach (var item in model.Items)
                {
                    if (DeserializeBlock(item) is Paragraph itemParagraph)
                    {
                        list.ListItems.Add(new ListItem(itemParagraph));
                    }
                }

                return list;
            }

            var paragraph = new Paragraph();
            foreach (var inline in model.Inlines)
            {
                if (inline.Kind == "line-break")
                {
                    list.ListItems.Add(new ListItem(paragraph));
                    paragraph = new Paragraph();
                }
                else
                {
                    paragraph.Inlines.Add(DeserializeInline(inline));
                }
            }
            if (paragraph.Inlines.Count > 0)
            {
                list.ListItems.Add(new ListItem(paragraph));
            }
            return list;
        }

        var result = new Paragraph { FontSize = model.FontSize is > 0 ? model.FontSize : 14 };
        var headingLevel = model.HeadingLevel is >= 0 and <= 3 ? model.HeadingLevel : 0;
        DocumentOutline.SetHeadingLevel(result, headingLevel);
        DocumentOutline.SetIsNavigationPoint(result, model.IsNavigationPoint);
        DocumentOutline.SetIsCollapsed(result, headingLevel > 0 && model.IsCollapsed);
        if (Enum.TryParse<TextAlignment>(model.Alignment, out var alignment))
        {
            result.TextAlignment = alignment;
        }
        foreach (var inline in model.Inlines)
        {
            result.Inlines.Add(DeserializeInline(inline));
        }
        if (model.LineSpacing is >= 0.8 and <= 3)
        {
            ParagraphLineSpacing.Apply(result, model.LineSpacing.Value);
        }
        return result;
    }

    private Inline DeserializeInline(InlineModel model)
    {
        if (model.Kind == "line-break")
        {
            return new LineBreak();
        }
        if (model.Kind == "image")
        {
            var image = new Image { Tag = model.Text, Width = model.Width is > 0 ? model.Width : 480, Stretch = Stretch.Uniform };
            var path = ResolveMediaPath(model.Text);
            if (path is not null && File.Exists(path))
            {
                image.Source = new BitmapImage(new Uri(path, UriKind.Absolute));
            }
            return new InlineUIContainer(image);
        }

        var run = new Run(model.Text ?? string.Empty);
        if (model.FontSize is > 0)
        {
            run.FontSize = model.FontSize;
        }
        if (model.Strikethrough)
        {
            run.TextDecorations = TextDecorations.Strikethrough;
        }
        if (!string.IsNullOrWhiteSpace(model.Foreground))
        {
            try
            {
                run.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(model.Foreground));
            }
            catch (FormatException)
            {
            }
        }
        Inline result = run;
        if (model.Underline)
        {
            result = new Underline(result);
        }
        if (model.Italic)
        {
            result = new Italic(result);
        }
        if (model.Bold)
        {
            result = new Bold(result);
        }
        if (model.InlineCode) result = new InlineCode(result);
        return result;
    }

    private string? ResolveMediaPath(string? relativePath)
    {
        if (_mediaRoot is null || string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }
        var root = Path.GetFullPath(_mediaRoot) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? candidate : null;
    }

    private static FlowDocument CreatePlainDocument(string? text)
    {
        var document = new FlowDocument();
        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
        {
            document.Blocks.Add(new Paragraph(new Run(line)));
        }
        return document;
    }

    private sealed class DocumentModel
    {
        public int Version { get; set; } = 1;
        public List<BlockModel> Blocks { get; set; } = new();
    }

    private sealed class BlockModel
    {
        public string? CodeText { get; set; }
        public string? CodeLanguage { get; set; }
        public bool WrapCode { get; set; } = true;
        public string Kind { get; set; } = "paragraph";
        public string? Alignment { get; set; }
        public double FontSize { get; set; } = 14;
        public double? LineSpacing { get; set; }
        public int HeadingLevel { get; set; }
        public bool IsNavigationPoint { get; set; }
        public bool IsCollapsed { get; set; }
        public List<BlockModel> Children { get; set; } = new();
        public List<BlockModel> Items { get; set; } = new();
        public List<InlineModel> Inlines { get; set; } = new();
    }

    private sealed record InlineModel
    {
        public string Kind { get; init; } = "text";
        public string Text { get; init; } = string.Empty;
        public bool Bold { get; init; }
        public bool Italic { get; init; }
        public bool Underline { get; init; }
        public bool Strikethrough { get; init; }
        public bool InlineCode { get; init; }
        public string? Foreground { get; init; }
        public double FontSize { get; init; }
        public double Width { get; init; }
    }
}
