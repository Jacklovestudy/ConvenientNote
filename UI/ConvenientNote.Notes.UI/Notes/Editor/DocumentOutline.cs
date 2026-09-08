using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;

namespace ConvenientNote.Services;

public sealed record OutlineEntry(Paragraph Paragraph, int Level, string Title, bool IsHeading, bool IsCollapsed);

/// <summary>Document metadata and logical traversal; no visual formatting or block ownership changes.</summary>
public static class DocumentOutline
{
    public static readonly DependencyProperty HeadingLevelProperty = DependencyProperty.RegisterAttached(
        "HeadingLevel", typeof(int), typeof(DocumentOutline), new FrameworkPropertyMetadata(0),
        static value => value is int level && level is >= 0 and <= 3);
    public static readonly DependencyProperty IsNavigationPointProperty = DependencyProperty.RegisterAttached(
        "IsNavigationPoint", typeof(bool), typeof(DocumentOutline), new FrameworkPropertyMetadata(false));
    public static readonly DependencyProperty IsCollapsedProperty = DependencyProperty.RegisterAttached(
        "IsCollapsed", typeof(bool), typeof(DocumentOutline), new FrameworkPropertyMetadata(false));

    public static int GetHeadingLevel(Paragraph paragraph) => (int)paragraph.GetValue(HeadingLevelProperty);
    public static void SetHeadingLevel(Paragraph paragraph, int level) => paragraph.SetValue(HeadingLevelProperty, level);
    public static bool GetIsNavigationPoint(Paragraph paragraph) => (bool)paragraph.GetValue(IsNavigationPointProperty);
    public static void SetIsNavigationPoint(Paragraph paragraph, bool value) => paragraph.SetValue(IsNavigationPointProperty, value);
    public static bool GetIsCollapsed(Paragraph paragraph) => (bool)paragraph.GetValue(IsCollapsedProperty);
    public static void SetIsCollapsed(Paragraph paragraph, bool value) => paragraph.SetValue(IsCollapsedProperty, value);

    public static IEnumerable<Block> LogicalBlocks(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            if (block is FoldedSection folded)
            {
                foreach (var hidden in LogicalBlocks(folded.HiddenDocument.Blocks)) yield return hidden;
            }
            else yield return block;
        }
    }

    public static IReadOnlyList<OutlineEntry> GetEntries(FlowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var entries = new List<OutlineEntry>();
        AddEntries(document.Blocks, entries, true);
        return entries;
    }

    private static void AddEntries(BlockCollection blocks, List<OutlineEntry> entries, bool topLevel)
    {
        foreach (var block in LogicalBlocks(blocks))
        {
            if (block is Paragraph paragraph)
            {
                var level = topLevel ? GetHeadingLevel(paragraph) : 0;
                if (level > 0)
                {
                    var title = string.Concat(paragraph.Inlines.Select(InlineText)).Trim();
                    var firstLine = title.IndexOfAny(['\r', '\n']);
                    if (firstLine >= 0) title = title[..firstLine];
                    if (title.Length == 0) title = "未命名章节";
                    entries.Add(new OutlineEntry(paragraph, level, title, level > 0, level > 0 && GetIsCollapsed(paragraph)));
                }
            }
            else if (block is List list)
            {
                foreach (var item in list.ListItems) AddEntries(item.Blocks, entries, false);
            }
            else if (block is Section section) AddEntries(section.Blocks, entries, false);
        }
    }

    private static string InlineText(Inline inline) => inline switch
    {
        Run run => run.Text,
        LineBreak => "\n",
        Span span => string.Concat(span.Inlines.Select(InlineText)),
        _ => string.Empty
    };

    public static IReadOnlyList<Block> GetSectionBlocks(FlowDocument document, Paragraph heading)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(heading);
        var level = GetHeadingLevel(heading);
        var result = new List<Block>();
        if (level == 0) return result;
        var found = false;
        foreach (var block in LogicalBlocks(document.Blocks))
        {
            if (!found) { found = ReferenceEquals(block, heading); continue; }
            if (block is Paragraph paragraph && GetHeadingLevel(paragraph) is var next && next > 0 && next <= level) break;
            result.Add(block);
        }
        return result;
    }

    public static Paragraph[] FindNumberedCandidates(FlowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return LogicalBlocks(document.Blocks).OfType<Paragraph>().Where(paragraph =>
        {
            var text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim();
            var match = Regex.Match(text, @"^[0-9]+(?:[.、]\s*|\s+)(?<title>\S.*)$");
            if (!match.Success) return false;
            var title = match.Groups["title"].Value;
            return !char.IsDigit(title[0]) && !Regex.IsMatch(title, @"[{};=]|//|/\*|```|^\s*(?:var|let|const|return|using|public|private)\b");
        }).ToArray();
    }
}
