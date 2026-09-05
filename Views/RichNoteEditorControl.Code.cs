using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ConvenientNote.Services;

namespace ConvenientNote.Views;

public partial class RichNoteEditorControl
{
    private bool _attachingCodeViews;
    private CodeBlockControl? _activeCodeView;

    private void InitializeCodeSupport()
    {
        Editor.AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(
            (_, e) => _activeCodeView = FindCodeBlockView(e.OriginalSource as DependencyObject)), true);
    }

    private void UndoEditorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeCodeView is { } view && VisibleCodeBlocks(Editor.Document.Blocks).Any(c => c.Child == view)) view.UndoCode();
        else if (Editor.CanUndo) Editor.Undo();
    }

    private void RedoEditorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeCodeView is { } view && VisibleCodeBlocks(Editor.Document.Blocks).Any(c => c.Child == view)) view.RedoCode();
        else if (Editor.CanRedo) Editor.Redo();
    }

    private static CodeBlockControl? FindCodeBlockView(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is CodeBlockControl view) return view;
            source = source is ContentElement content ? ContentOperations.GetParent(content) : VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private void AttachCodeBlockViews()
    {
        if (_attachingCodeViews || Editor is null) return;
        _attachingCodeViews = true;
        try
        {
            foreach (var code in VisibleCodeBlocks(Editor.Document.Blocks).ToList())
            {
                if (code.Child is not CodeBlockControl view)
                {
                    view = new CodeBlockControl();
                    view.ContentEdited += (_, _) =>
                    {
                        UpdateWordCount();
                        if (!_isLoading) ScheduleSave();
                    };
                    view.ExitRequested += (sender, _) =>
                    {
                        if (sender is CodeBlockControl { Block: { } current }) ExitCodeBlock(current);
                    };
                    view.Attach(code);
                    code.Child = view;
                }
                else if (view.Block != code) view.Attach(code);
            }
        }
        finally { _attachingCodeViews = false; }
    }

    private static IEnumerable<CodeBlock> VisibleCodeBlocks(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            if (block is CodeBlock code) yield return code;
            else if (block is Section section)
                foreach (var child in VisibleCodeBlocks(section.Blocks)) yield return child;
            else if (block is System.Windows.Documents.List list)
                foreach (var item in list.ListItems)
                    foreach (var child in VisibleCodeBlocks(item.Blocks)) yield return child;
        }
    }

    private void InsertCodeBlockButton_Click(object sender, RoutedEventArgs e) => InsertCodeBlock();

    internal void InsertCodeBlock()
    {
        if (FindCodeBlockView(Keyboard.FocusedElement as DependencyObject) is CodeBlockControl { Block: { } focused }) ExitCodeBlock(focused);
        ExpandForEditing();
        if (Editor.Selection.Start.Paragraph is not Paragraph start || start.Parent != Editor.Document ||
            Editor.Selection.End.Paragraph is not Paragraph end || end.Parent != Editor.Document)
        {
            OutlineStatusText.Text = "请在独立正文段落插入代码，或选择要转换的代码文字。";
            return;
        }
        // Never replace an embedded image, list or existing code editor with its
        // plain-text placeholder during conversion.
        var pointer = Editor.Selection.Start;
        while (pointer.CompareTo(Editor.Selection.End) < 0)
        {
            if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.EmbeddedElement)
            { OutlineStatusText.Text = "所选内容含图片或代码块，请只选择代码文字。"; return; }
            if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart &&
                pointer.GetAdjacentElement(LogicalDirection.Forward) is System.Windows.Documents.List or Table)
            { OutlineStatusText.Text = "请先取消列表或表格格式，再转换为代码块。"; return; }
            pointer = pointer.GetNextContextPosition(LogicalDirection.Forward)!;
        }
        var text = Editor.Selection.Text;
        CodeBlock? code = null;
        OutlineChange(() =>
        {
            Editor.Selection.Text = "";
            var before = Editor.Selection.Start.Paragraph!;
            var next = Editor.Selection.Start.InsertParagraphBreak();
            var after = next.Paragraph!;
            DocumentOutline.SetHeadingLevel(after, 0);
            DocumentOutline.SetIsCollapsed(after, false);
            after.FontSize = 15;
            code = new CodeBlock { CodeText = text, CodeLanguage = "C#", WrapCode = true, Margin = new Thickness(0, 8, 0, 12) };
            Editor.Document.Blocks.InsertBefore(after, code);
            if (DocumentOutline.GetHeadingLevel(before) == 0 && string.IsNullOrEmpty(new TextRange(before.ContentStart, before.ContentEnd).Text) && !before.Inlines.OfType<InlineUIContainer>().Any())
                Editor.Document.Blocks.Remove(before);
            Editor.CaretPosition = after.ContentStart;
        });
        AttachCodeBlockViews();
        if (code?.Child is CodeBlockControl view) view.SelectCode(0, 0);
    }

    private void ExitCodeBlock(CodeBlock block)
    {
        if (block.Parent != Editor.Document) { Editor.Focus(); return; }
        if (block.NextBlock is not Paragraph)
            OutlineChange(() => Editor.Document.Blocks.InsertAfter(block, new Paragraph { FontSize = 15 }));
        Editor.Focus();
        Editor.CaretPosition = ((Paragraph)block.NextBlock!).ContentStart;
    }

    private void InlineCodeButton_Click(object sender, RoutedEventArgs e) => ApplyInlineCode();

    internal void ApplyInlineCode()
    {
        if (Editor.Selection.IsEmpty || Editor.Selection.Start.Paragraph != Editor.Selection.End.Paragraph)
        { OutlineStatusText.Text = "先选中同一段里的文字，再点“行内代码”。"; return; }
        OutlineChange(() =>
        {
            var existing = Editor.Selection.Start.Parent as Inline;
            while (existing is not null && existing is not InlineCode) existing = existing.Parent as Inline;
            if (existing is InlineCode code && code.ContentEnd.CompareTo(Editor.Selection.End) >= 0)
            {
                var inlines = code.Parent switch { Paragraph p => p.Inlines, Span s => s.Inlines, _ => null };
                if (inlines is not null)
                {
                    code.RestoreFontFamilies();
                    foreach (var child in code.Inlines.ToList()) { code.Inlines.Remove(child); inlines.InsertBefore(code, child); }
                    inlines.Remove(code);
                }
            }
            else
            {
                _ = new InlineCode(Editor.Selection.Start, Editor.Selection.End);
            }
        });
        Editor.Focus();
    }

    private static IEnumerable<Block> LogicalSearchTargets(BlockCollection blocks)
    {
        foreach (var block in DocumentOutline.LogicalBlocks(blocks))
        {
            if (block is Paragraph or CodeBlock) yield return block;
            else if (block is Section section)
                foreach (var child in LogicalSearchTargets(section.Blocks)) yield return child;
            else if (block is System.Windows.Documents.List list)
                foreach (var item in list.ListItems)
                    foreach (var child in LogicalSearchTargets(item.Blocks)) yield return child;
            else if (block is Table table)
                foreach (var group in table.RowGroups)
                    foreach (var row in group.Rows)
                        foreach (var cell in row.Cells)
                            foreach (var child in LogicalSearchTargets(cell.Blocks)) yield return child;
        }
    }

    private static List<int> CodeMatches(string text, string query)
    {
        var positions = new List<int>();
        for (var offset = 0; offset <= text.Length - query.Length;)
        {
            var found = text.IndexOf(query, offset, StringComparison.OrdinalIgnoreCase);
            if (found < 0) break;
            positions.Add(found); offset = found + query.Length;
        }
        return positions;
    }

    private bool FindTextOrCode(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) { OutlineStatusText.Text = "输入要查找的正文内容"; return false; }
        var targets = LogicalSearchTargets(Editor.Document.Blocks).ToList();
        var matches = new List<(int Target, int Occurrence)>();
        for (var i = 0; i < targets.Count; i++)
        {
            var count = targets[i] is CodeBlock code ? CodeMatches(code.CodeText, query).Count : FindParagraphMatches((Paragraph)targets[i], query).Count;
            for (var j = 0; j < count; j++) matches.Add((i, j));
        }
        if (matches.Count == 0) { OutlineStatusText.Text = "未找到匹配内容"; return false; }
        _lastFindIndex = query == _lastFindQuery ? (_lastFindIndex + 1) % matches.Count : 0;
        _lastFindQuery = query;
        var location = matches[_lastFindIndex];
        while (true)
        {
            var target = LogicalSearchTargets(Editor.Document.Blocks).ElementAt(location.Target);
            var folded = Editor.Document.Blocks.OfType<FoldedSection>().FirstOrDefault(f => LogicalSearchTargets(f.HiddenDocument.Blocks).Contains(target));
            if (folded is null) break;
            OutlineChange(() => ExpandSectionCore(folded, true));
            if (folded.Parent == Editor.Document) return false;
        }
        AttachCodeBlockViews();
        var foundTarget = LogicalSearchTargets(Editor.Document.Blocks).ElementAt(location.Target);
        foundTarget.BringIntoView();
        if (foundTarget is CodeBlock foundCode && foundCode.Child is CodeBlockControl view)
            view.SelectCode(CodeMatches(foundCode.CodeText, query)[location.Occurrence], query.Length);
        else if (foundTarget is Paragraph paragraph)
        {
            var match = FindParagraphMatches(paragraph, query)[location.Occurrence];
            Editor.Focus(); Editor.Selection.Select(match.Start, match.End);
        }
        OutlineStatusText.Text = $"第 {_lastFindIndex + 1} / {matches.Count} 处匹配";
        return true;
    }

    private bool TryCopyCodeSelection(ExecutedRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Copy && e.Command != ApplicationCommands.Cut) return false;
        var start = Editor.Selection.Start;
        var end = Editor.Selection.End;
        var codes = VisibleCodeBlocks(Editor.Document.Blocks).Where(c => c.ElementStart.CompareTo(end) < 0 && c.ElementEnd.CompareTo(start) > 0).ToList();
        if (codes.Count == 0) return false;
        var text = new System.Text.StringBuilder();
        var cursor = start;
        foreach (var code in codes)
        {
            if (cursor.CompareTo(code.ElementStart) < 0) text.Append(new TextRange(cursor, code.ElementStart).Text);
            text.Append(code.CodeText);
            if (!code.CodeText.EndsWith('\n')) text.AppendLine();
            cursor = code.ElementEnd;
        }
        if (cursor.CompareTo(end) < 0) text.Append(new TextRange(cursor, end).Text);
        e.Handled = true;
        try
        {
            Clipboard.SetText(text.ToString());
            if (e.Command == ApplicationCommands.Cut) Editor.Selection.Text = "";
            OutlineStatusText.Text = "已复制文字和代码（纯文本）。";
        }
        catch (System.Runtime.InteropServices.ExternalException) { OutlineStatusText.Text = "剪贴板暂时不可用，请重试。"; }
        return true;
    }
}
