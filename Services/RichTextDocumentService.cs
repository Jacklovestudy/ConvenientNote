using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
            Blocks = document.Blocks.Select(SerializeBlock).Where(static block => block is not null).Cast<BlockModel>().ToList()
        };
        var mediaPaths = model.Blocks
            .SelectMany(static block => block.Inlines)
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
        return new TextRange(document.ContentStart, document.ContentEnd).Text.TrimEnd('\r', '\n');
    }

    private static BlockModel? SerializeBlock(Block block)
    {
        if (block is Paragraph paragraph)
        {
            return new BlockModel
            {
                Kind = "paragraph",
                Alignment = paragraph.TextAlignment.ToString(),
                FontSize = paragraph.FontSize,
                Inlines = paragraph.Inlines.SelectMany(SerializeInline).ToList()
            };
        }

        if (block is List list)
        {
            return new BlockModel
            {
                Kind = list.MarkerStyle == TextMarkerStyle.Decimal ? "numbered-list" : "bullet-list",
                Inlines = list.ListItems
                    .SelectMany(static item => item.Blocks.OfType<Paragraph>())
                    .SelectMany(static paragraph => paragraph.Inlines.SelectMany(SerializeInline).Append(new InlineModel { Kind = "line-break" }))
                    .ToList()
            };
        }

        return null;
    }

    private static IEnumerable<InlineModel> SerializeInline(Inline inline)
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
                    Underline = run.TextDecorations == TextDecorations.Underline,
                    Strikethrough = run.TextDecorations == TextDecorations.Strikethrough,
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
        if (model.Kind is "bullet-list" or "numbered-list")
        {
            var list = new List
            {
                MarkerStyle = model.Kind == "numbered-list" ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc
            };
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
        if (Enum.TryParse<TextAlignment>(model.Alignment, out var alignment))
        {
            result.TextAlignment = alignment;
        }
        foreach (var inline in model.Inlines)
        {
            result.Inlines.Add(DeserializeInline(inline));
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
        public string Kind { get; set; } = "paragraph";
        public string? Alignment { get; set; }
        public double FontSize { get; set; } = 14;
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
        public string? Foreground { get; init; }
        public double FontSize { get; init; }
        public double Width { get; init; }
    }
}
