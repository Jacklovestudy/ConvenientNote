using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ConvenientNote.Services;

namespace ConvenientNote.Views;

public partial class RichNoteEditorControl
{
    private bool _changingOutline;
    private bool _updatingParagraphStyle;
    private bool _outlineRefreshQueued;
    private bool _headingPositionsDirty = true;
    private bool _headingPositionUpdateQueued;
    private readonly RichTextDocumentService _outlineDocuments = new();
    private readonly List<Button> _headingButtons = new();
    private string _lastFindQuery = "";
    private int _lastFindIndex = -1;
    private Paragraph? _activeOutlineParagraph;
    private sealed class UnsupportedFoldContentException(Exception inner) : Exception("Cannot clone this document content.", inner);

    private FlowDocument CloneOutlineBlocks(IEnumerable<Block> blocks)
    {
        try { return _outlineDocuments.CloneBlocks(blocks); }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or System.Windows.Markup.XamlParseException or ArgumentException)
        { throw new UnsupportedFoldContentException(error); }
    }

    private sealed record OutlineRow(OutlineEntry Entry)
    {
        public string Title => Entry.Title;
        public string Arrow => Entry.IsCollapsed ? "▶" : "▼";
        public Thickness Indent => new(Math.Max(0, Entry.Level - 1) * 12, 0, 0, 0);
        public Visibility FoldVisibility => Entry.IsHeading ? Visibility.Visible : Visibility.Hidden;
    }

    private void InitializeOutline()
    {
        Editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => QueueHeadingPositionUpdate()));
        Editor.SizeChanged += (_, _) => QueueHeadingPositionUpdate();
        Editor.Loaded += (_, _) => QueueHeadingPositionUpdate();
        // LayoutUpdated is global to the dispatcher: drawer animation and other
        // unrelated controls raise it too. Only retry work invalidated by the editor.
        Editor.LayoutUpdated += (_, _) =>
        {
            if (_headingPositionsDirty) UpdateHeadingPositions();
        };
        CommandManager.AddPreviewExecutedHandler(Editor, Editor_PreviewExecuted);
        Editor.PreviewTextInput += (_, e) =>
        {
            if (FindCodeBlockView(e.OriginalSource as DependencyObject) is null) ExpandForEditing();
        };
        DataObject.AddPastingHandler(Editor, (_, e) =>
        {
            if (FindCodeBlockView(e.OriginalSource as DependencyObject) is null) ExpandForEditing();
        });
        Editor.PreviewMouseMove += (_, e) =>
        {
            if (FindCodeBlockView(e.OriginalSource as DependencyObject) is not null) return;
            // A drag can otherwise transfer only the presentation placeholder.
            if (e.LeftButton == MouseButtonState.Pressed && !Editor.Selection.IsEmpty)
                ExpandForEditing();
        };
        RefreshOutline();
    }

    private void QueueOutlineRefresh()
    {
        if (_changingOutline || _outlineRefreshQueued || OutlineList is null) return;
        _outlineRefreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _outlineRefreshQueued = false;
            RefreshOutline();
        }));
    }

    private void RefreshOutline()
    {
        if (_changingOutline || OutlineList is null) return;
        SynchronizeCollapsedFlags();
        var entries = DocumentOutline.GetEntries(Editor.Document);
        OutlineList.ItemsSource = entries.Select(e => new OutlineRow(e)).ToList();
        _activeOutlineParagraph = null;
        OutlineEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HeadingGutter.Children.Clear();
        _headingButtons.Clear();
        foreach (var paragraph in Editor.Document.Blocks.OfType<Paragraph>().Where(p => DocumentOutline.GetHeadingLevel(p) > 0))
        {
            var button = new Button
            {
                Content = DocumentOutline.GetIsCollapsed(paragraph) ? "▶" : "▼",
                Tag = paragraph, Width = 23, Height = 23, Padding = new Thickness(0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(63, 81, 181)),
                ToolTip = "展开或折叠章节 (Ctrl+Alt+Left / Right)", Focusable = false
            };
            System.Windows.Automation.AutomationProperties.SetName(button,
                $"{(DocumentOutline.GetIsCollapsed(paragraph) ? "展开" : "折叠")} {new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim()}");
            button.Click += (_, _) => ToggleSection(paragraph);
            HeadingGutter.Children.Add(button);
            _headingButtons.Add(button);
        }
        UpdateParagraphStyleSelection();
        UpdateHeadingPositions();
    }

    private void SynchronizeCollapsedFlags()
    {
        // Undo/redo restores presentation blocks. Derive visible flags from those
        // blocks rather than from stale object references or previous UI state.
        foreach (var paragraph in Editor.Document.Blocks.OfType<Paragraph>())
        {
            var folded = paragraph.NextBlock as FoldedSection;
            DocumentOutline.SetIsCollapsed(paragraph, folded is not null && DocumentOutline.GetHeadingLevel(paragraph) > 0);
            if (folded is not null) folded.Heading = paragraph;
        }
    }

    private void QueueHeadingPositionUpdate()
    {
        _headingPositionsDirty = true;
        if (_headingPositionUpdateQueued) return;
        _headingPositionUpdateQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _headingPositionUpdateQueued = false;
            if (_headingPositionsDirty) UpdateHeadingPositions();
        }));
    }

    private void UpdateHeadingPositions()
    {
        // Preserve retries when called before document reflow has finished (and
        // allow explicit callers such as zoom/outline refresh to invalidate it).
        _headingPositionsDirty = true;
        if (_changingOutline || !Editor.IsLoaded || !Editor.IsMeasureValid || !Editor.IsArrangeValid) return;
        _headingPositionsDirty = false;
        Paragraph? active = null;
        foreach (var button in _headingButtons)
        {
            if (button.Tag is not Paragraph p || p.Parent != Editor.Document) continue;
            var rect = p.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            if (rect.IsEmpty) { button.Visibility = Visibility.Hidden; continue; }
            var y = rect.Top;
            button.Visibility = y >= 0 && y < Editor.ActualHeight - 16 ? Visibility.Visible : Visibility.Hidden;
            Canvas.SetTop(button, y);
            if (y <= 52) active = p;
        }
        active ??= _headingButtons.FirstOrDefault()?.Tag as Paragraph;
        if (active is not null && active != _activeOutlineParagraph && OutlineList.ItemsSource is IEnumerable<OutlineRow> rows)
        {
            OutlineList.SelectedItem = rows.FirstOrDefault(r => r.Entry.Paragraph == active);
            _activeOutlineParagraph = active;
        }
    }

    private void OutlineChange(Action action)
    {
        if (_changingOutline) return;
        _changingOutline = true;
        Editor.BeginChange();
        try { action(); }
        catch (UnsupportedFoldContentException)
        {
            OutlineStatusText.Text = "这段内容包含暂不支持折叠的对象，原文已保留。";
        }
        finally
        {
            Editor.EndChange();
            _changingOutline = false;
        }
        RefreshOutline();
        UpdateWordCount();
        if (!_isLoading) ScheduleSave();
    }

    internal void ToggleSection(Paragraph heading)
    {
        if (heading.Parent != Editor.Document || DocumentOutline.GetHeadingLevel(heading) == 0) return;
        OutlineChange(() =>
        {
            if (heading.NextBlock is FoldedSection folded) ExpandSectionCore(folded, true);
            else CollapseSectionCore(heading);
        });
    }

    private void CollapseSectionCore(Paragraph heading)
    {
        if (heading.NextBlock is FoldedSection || heading.Parent != Editor.Document) return;
        var level = DocumentOutline.GetHeadingLevel(heading);
        if (level == 0) return;
        var body = new List<Block>();
        for (var block = heading.NextBlock; block is not null; block = block.NextBlock)
        {
            if (block is Paragraph next && DocumentOutline.GetHeadingLevel(next) is var nextLevel && nextLevel > 0 && nextLevel <= level) break;
            body.Add(block);
        }
        if (body.Count == 0) return;
        var snapshot = CloneOutlineBlocks(body);
        var placeholder = new FoldedSection(heading, snapshot) { Margin = new Thickness(0, 2, 0, 10) };
        var count = DocumentOutline.LogicalBlocks(snapshot.Blocks).Count();
        var expand = new Button
        {
            Content = $"▶ 已折叠 {count} 段内容，点击展开", HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 4, 10, 4), FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(242, 244, 250)), BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(63, 81, 181)), Focusable = false
        };
        expand.Click += (sender, _) =>
        {
            var current = Editor.Document.Blocks.OfType<FoldedSection>().FirstOrDefault(f => ReferenceEquals(f.Child, sender));
            if (current is not null) OutlineChange(() => ExpandSectionCore(current, true));
        };
        placeholder.Child = expand;
        // Keep a collapsed selection out of the body before removing it.
        Editor.Selection.Select(heading.ContentEnd, heading.ContentEnd);
        foreach (var block in body) Editor.Document.Blocks.Remove(block);
        Editor.Document.Blocks.InsertAfter(heading, placeholder);
        DocumentOutline.SetIsCollapsed(heading, true);
    }

    private void ExpandSectionCore(FoldedSection folded, bool restoreNested)
    {
        if (folded.Parent != Editor.Document) return;
        var clone = CloneOutlineBlocks(folded.HiddenDocument.Blocks);
        var blocks = clone.Blocks.ToList();
        var collapsed = blocks.OfType<Paragraph>().Where(DocumentOutline.GetIsCollapsed).ToList();
        foreach (var block in blocks)
        {
            clone.Blocks.Remove(block);
            Editor.Document.Blocks.InsertBefore(folded, block);
        }
        if (folded.PreviousBlock is Paragraph previous && previous == folded.Heading)
            DocumentOutline.SetIsCollapsed(previous, false);
        DocumentOutline.SetIsCollapsed(folded.Heading, false);
        Editor.Document.Blocks.Remove(folded);
        if (restoreNested)
            foreach (var heading in collapsed.AsEnumerable().Reverse()) CollapseSectionCore(heading);
        else
            foreach (var paragraph in blocks.OfType<Paragraph>()) DocumentOutline.SetIsCollapsed(paragraph, false);
    }

    internal void RestoreSavedFolds()
    {
        _lastFindIndex = -1;
        _lastFindQuery = "";
        NumberedCandidatesPanel.Visibility = Visibility.Collapsed;
        NumberedCandidatesList.Children.Clear();
        var headings = Editor.Document.Blocks.OfType<Paragraph>().Where(DocumentOutline.GetIsCollapsed).ToList();
        if (headings.Count > 0)
            OutlineChange(() => { foreach (var heading in headings.AsEnumerable().Reverse()) CollapseSectionCore(heading); });
        else RefreshOutline();
        // Loading is the start of a note's undo history; presentation restoration
        // must not let Undo erase restored content from a previous session.
        Editor.IsUndoEnabled = false;
        Editor.IsUndoEnabled = true;
    }

    private void ExpandAllCore()
    {
        foreach (var folded in Editor.Document.Blocks.OfType<FoldedSection>().ToList()) ExpandSectionCore(folded, false);
        foreach (var paragraph in Editor.Document.Blocks.OfType<Paragraph>()) DocumentOutline.SetIsCollapsed(paragraph, false);
    }

    private void ExpandForEditing()
    {
        if (_changingOutline || !Editor.Document.Blocks.OfType<FoldedSection>().Any()) return;
        var start = Editor.Selection.Start;
        var end = Editor.Selection.End;
        var intersecting = Editor.Document.Blocks.OfType<FoldedSection>().Where(f =>
            f.ElementStart.CompareTo(end) <= 0 && f.ElementEnd.CompareTo(start) >= 0).ToList();
        if (intersecting.Count == 0) return;
        OutlineChange(() =>
        {
            foreach (var folded in intersecting) ExpandSectionCore(folded, false);
            Editor.Selection.Select(start, end);
        });
    }

    private void Editor_PreviewExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (FindCodeBlockView(e.OriginalSource as DependencyObject) is not null) return;
        if (e.Command == ApplicationCommands.Undo || e.Command == ApplicationCommands.Redo) return;
        if (e.Command == EditingCommands.EnterParagraphBreak && TryEnterAfterHeading()) { e.Handled = true; return; }
        if (e.Command == EditingCommands.EnterParagraphBreak) ExpandAtEditingBoundary(Key.Enter);
        if (e.Command == EditingCommands.Delete || e.Command == EditingCommands.DeleteNextWord) ExpandAtEditingBoundary(Key.Delete);
        if (e.Command == EditingCommands.Backspace || e.Command == EditingCommands.DeletePreviousWord) ExpandAtEditingBoundary(Key.Back);
        if (e.Command == ApplicationCommands.SelectAll)
        {
            if (Editor.Document.Blocks.OfType<FoldedSection>().Any()) OutlineChange(ExpandAllCore);
            return;
        }
        ExpandForEditing();
        if (TryCopyCodeSelection(e)) return;
    }

    private bool TryEnterAfterHeading()
    {
        if (_changingOutline || !Editor.Selection.IsEmpty || Editor.CaretPosition.Paragraph is not Paragraph heading ||
            DocumentOutline.GetHeadingLevel(heading) == 0 ||
            !string.IsNullOrEmpty(new TextRange(Editor.CaretPosition, heading.ContentEnd).Text)) return false;
        OutlineChange(() =>
        {
            if (heading.NextBlock is FoldedSection folded) ExpandSectionCore(folded, false);
            var next = heading.ContentEnd.InsertParagraphBreak();
            var paragraph = next.Paragraph!;
            DocumentOutline.SetHeadingLevel(paragraph, 0);
            DocumentOutline.SetIsNavigationPoint(paragraph, false);
            DocumentOutline.SetIsCollapsed(paragraph, false);
            paragraph.FontSize = 15;
            foreach (var inline in paragraph.Inlines) inline.FontSize = 15;
            ParagraphLineSpacing.Refresh(paragraph);
            Editor.Selection.Select(next, next);
            Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, 15d);
        });
        return true;
    }

    private void ExpandAtEditingBoundary(Key key)
    {
        ExpandForEditing();
        if (!Editor.Selection.IsEmpty || Editor.CaretPosition.Paragraph is not Paragraph paragraph) return;
        var atStart = string.IsNullOrEmpty(new TextRange(paragraph.ContentStart, Editor.CaretPosition).Text);
        var atEnd = string.IsNullOrEmpty(new TextRange(Editor.CaretPosition, paragraph.ContentEnd).Text);
        FoldedSection? folded = null;
        if (key == Key.Back && atStart) folded = paragraph.PreviousBlock as FoldedSection;
        if ((key == Key.Delete && atEnd) || key == Key.Enter) folded = paragraph.NextBlock as FoldedSection;
        if (folded is not null)
        {
            var caret = Editor.CaretPosition;
            OutlineChange(() => { ExpandSectionCore(folded, false); Editor.CaretPosition = caret; });
        }
    }

    private void CollapseAllButton_Click(object sender, RoutedEventArgs e) => OutlineChange(() =>
    {
        foreach (var heading in Editor.Document.Blocks.OfType<Paragraph>().Where(p => DocumentOutline.GetHeadingLevel(p) > 0).Reverse().ToList())
            CollapseSectionCore(heading);
    });

    private void ExpandAllButton_Click(object sender, RoutedEventArgs e) => OutlineChange(ExpandAllCore);

    private void SetHeadingButton_Click(object sender, RoutedEventArgs e) => SetCurrentHeading(1);

    private void SetCurrentHeading(int level)
    {
        var paragraph = Editor.CaretPosition.Paragraph;
        if (paragraph is null) return;
        if (paragraph.Parent != Editor.Document)
        {
            OutlineStatusText.Text = "章节标题需独立成段，请先取消该段的列表格式。";
            return;
        }
        OutlineChange(() =>
        {
            ExpandAllCore();
            ReplaceParagraphMetadata(paragraph, replacement =>
            {
                DocumentOutline.SetHeadingLevel(replacement, level);
                DocumentOutline.SetIsNavigationPoint(replacement, false);
                var range = new TextRange(replacement.ContentStart, replacement.ContentEnd);
                range.ApplyPropertyValue(TextElement.FontSizeProperty, level switch { 1 => 28d, 2 => 22d, 3 => 18d, _ => 15d });
                ParagraphLineSpacing.Refresh(replacement);
            });
        });
        RefreshFontSizeSelection();
        Editor.Focus();
    }

    private void UpdateParagraphStyleSelection()
    {
        if (Editor.CaretPosition.Paragraph is not Paragraph paragraph) return;
        _updatingParagraphStyle = true;
        try { ParagraphStyleComboBox.SelectedIndex = DocumentOutline.GetHeadingLevel(paragraph); }
        finally { _updatingParagraphStyle = false; }
    }

    private void ReplaceParagraphMetadata(Paragraph paragraph, Action<Paragraph> update)
    {
        var collection = paragraph.Parent switch
        {
            FlowDocument document => document.Blocks,
            Section section => section.Blocks,
            ListItem item => item.Blocks,
            TableCell cell => cell.Blocks,
            _ => null
        };
        if (collection is null) return;
        var snapshot = CloneOutlineBlocks(new[] { paragraph });
        var replacement = (Paragraph)snapshot.Blocks.FirstBlock;
        update(replacement);
        var startOffset = paragraph.ContentStart.GetOffsetToPosition(Editor.Selection.Start);
        var endOffset = paragraph.ContentStart.GetOffsetToPosition(Editor.Selection.End);
        snapshot.Blocks.Remove(replacement);
        collection.InsertBefore(paragraph, replacement);
        collection.Remove(paragraph);
        // Metadata itself is not a WPF text undo unit. Replacing the paragraph
        // records both its old content/properties and its new properties together.
        var length = replacement.ContentStart.GetOffsetToPosition(replacement.ContentEnd);
        Editor.Selection.Select(
            replacement.ContentStart.GetPositionAtOffset(Math.Clamp(startOffset, 0, length))!,
            replacement.ContentStart.GetPositionAtOffset(Math.Clamp(endOffset, 0, length))!);
    }

    private void OutlineFoldButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: OutlineRow row })
        {
            var index = DocumentOutline.GetEntries(Editor.Document).ToList().FindIndex(x => x.Paragraph == row.Entry.Paragraph);
            if (row.Entry.Paragraph.Parent != Editor.Document && !RevealEntry(index)) return;
            var entries = DocumentOutline.GetEntries(Editor.Document);
            if (index >= 0 && index < entries.Count) ToggleSection(entries[index].Paragraph);
        }
        e.Handled = true;
    }

    private void OutlineList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        for (DependencyObject? node = e.OriginalSource as DependencyObject; node is not null;
             node = node is ContentElement content ? ContentOperations.GetParent(content) : VisualTreeHelper.GetParent(node))
        {
            if (node is Button) return;
            if (node is ListBoxItem { DataContext: OutlineRow row }) { NavigateTo(row.Entry); return; }
            if (node == OutlineList) return;
        }
    }

    private void OutlineList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && OutlineList.SelectedItem is OutlineRow row) { NavigateTo(row.Entry); e.Handled = true; }
    }

    private void NavigateTo(OutlineEntry entry)
    {
        var entries = DocumentOutline.GetEntries(Editor.Document);
        var index = entries.ToList().FindIndex(x => x.Paragraph == entry.Paragraph);
        if (!RevealEntry(index)) return;
        entries = DocumentOutline.GetEntries(Editor.Document);
        if (index < 0 || index >= entries.Count) return;
        var target = entries[index].Paragraph;
        // Clicking a folded heading reveals its body as well as its ancestors.
        if (target.NextBlock is FoldedSection folded) OutlineChange(() => ExpandSectionCore(folded, true));
        Editor.Focus();
        Editor.Selection.Select(target.ContentStart, target.ContentEnd);
        target.BringIntoView();
    }

    private bool RevealEntry(int index)
    {
        if (index < 0) return false;
        while (true)
        {
            var entries = DocumentOutline.GetEntries(Editor.Document);
            if (index >= entries.Count) return false;
            var paragraph = entries[index].Paragraph;
            var folded = Editor.Document.Blocks.OfType<FoldedSection>().FirstOrDefault(f =>
                DocumentOutline.GetEntries(f.HiddenDocument).Any(x => x.Paragraph == paragraph));
            if (folded is null) return true;
            OutlineChange(() => ExpandSectionCore(folded, true));
            if (folded.Parent == Editor.Document) return false;
        }
    }

    private bool HandleOutlineKey(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.Control && key == Key.F)
        {
            SidebarTabs.SelectedIndex = 0;
            DocumentSearchBox.Focus(); DocumentSearchBox.SelectAll(); return true;
        }
        if (key == Key.F3) { FindInDocument(DocumentSearchBox.Text); return true; }
        if (modifiers != (ModifierKeys.Control | ModifierKeys.Alt)) return false;
        if (key >= Key.D0 && key <= Key.D3) { SetCurrentHeading(key - Key.D0); return true; }
        if (key is Key.Left or Key.Right)
        {
            var current = Editor.CaretPosition.Paragraph;
            var heading = Editor.Document.Blocks.OfType<Paragraph>().LastOrDefault(p =>
                DocumentOutline.GetHeadingLevel(p) > 0 && current is not null && p.ContentStart.CompareTo(current.ContentStart) <= 0);
            if (heading is not null && (key == Key.Right) == (heading.NextBlock is FoldedSection)) ToggleSection(heading);
            return true;
        }
        return false;
    }

    private void FindNextButton_Click(object sender, RoutedEventArgs e) => FindInDocument(DocumentSearchBox.Text);
    private void DocumentSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { FindInDocument(DocumentSearchBox.Text); e.Handled = true; }
    }

    internal bool FindInDocument(string query) => FindTextOrCode(query);

    private static IEnumerable<Paragraph> LogicalParagraphs(BlockCollection blocks)
    {
        foreach (var block in DocumentOutline.LogicalBlocks(blocks))
        {
            if (block is Paragraph paragraph) yield return paragraph;
            else if (block is Section section)
                foreach (var child in LogicalParagraphs(section.Blocks)) yield return child;
            else if (block is System.Windows.Documents.List list)
                foreach (var item in list.ListItems)
                    foreach (var child in LogicalParagraphs(item.Blocks)) yield return child;
            else if (block is Table table)
                foreach (var group in table.RowGroups)
                    foreach (var row in group.Rows)
                        foreach (var cell in row.Cells)
                            foreach (var child in LogicalParagraphs(cell.Blocks)) yield return child;
        }
    }

    private static List<(TextPointer Start, TextPointer End)> FindParagraphMatches(Paragraph paragraph, string query)
    {
        var matches = new List<(TextPointer Start, TextPointer End)>();
        {
            var text = new System.Text.StringBuilder();
            var starts = new List<TextPointer>();
            var ends = new List<TextPointer>();
            var pointer = paragraph.ContentStart;
            while (pointer is not null && pointer.CompareTo(paragraph.ContentEnd) < 0)
            {
                if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    var run = pointer.GetTextInRun(LogicalDirection.Forward);
                    for (var i = 0; i < run.Length; i++)
                    {
                        text.Append(run[i]); starts.Add(pointer.GetPositionAtOffset(i)!); ends.Add(pointer.GetPositionAtOffset(i + 1)!);
                    }
                }
                else if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart && pointer.GetAdjacentElement(LogicalDirection.Forward) is LineBreak)
                {
                    text.Append('\n'); starts.Add(pointer); ends.Add(pointer.GetNextContextPosition(LogicalDirection.Forward)!);
                }
                pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
            }
            var value = text.ToString();
            for (var offset = 0; offset <= value.Length - query.Length;)
            {
                var found = value.IndexOf(query, offset, StringComparison.OrdinalIgnoreCase);
                if (found < 0) break;
                matches.Add((starts[found], ends[found + query.Length - 1]));
                offset = found + Math.Max(1, query.Length);
            }
        }
        return matches;
    }

    private void FindNumberedHeadingsButton_Click(object sender, RoutedEventArgs e)
    {
        OutlineChange(ExpandAllCore);
        NumberedCandidatesList.Children.Clear();
        var candidates = DocumentOutline.FindNumberedCandidates(Editor.Document).Where(p => DocumentOutline.GetHeadingLevel(p) == 0).ToList();
        if (candidates.Count == 0) { OutlineStatusText.Text = "未找到新的编号段落，如“12 内存泄漏”。"; return; }
        foreach (var paragraph in candidates)
        {
            var text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim();
            NumberedCandidatesList.Children.Add(new CheckBox
            {
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                Tag = paragraph, IsChecked = true, Margin = new Thickness(0, 5, 0, 5)
            });
        }
        NumberedCandidatesPanel.Visibility = Visibility.Visible;
    }

    private void ConfirmNumberedHeadingsButton_Click(object sender, RoutedEventArgs e)
    {
        var chosen = NumberedCandidatesList.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => c.Tag).OfType<Paragraph>().ToList();
        OutlineChange(() =>
        {
            foreach (var paragraph in chosen.Where(p => p.Parent == Editor.Document))
                ReplaceParagraphMetadata(paragraph, replacement => DocumentOutline.SetHeadingLevel(replacement, 1));
        });
        NumberedCandidatesPanel.Visibility = Visibility.Collapsed;
        NumberedCandidatesList.Children.Clear();
        OutlineStatusText.Text = $"已生成 {chosen.Count} 个章节，保留原有文字格式。";
    }

    private void CancelNumberedHeadingsButton_Click(object sender, RoutedEventArgs e)
    {
        NumberedCandidatesPanel.Visibility = Visibility.Collapsed;
        NumberedCandidatesList.Children.Clear();
    }
}
