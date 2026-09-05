using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ConvenientNote.Services;
using ConvenientNote.ViewModels;

namespace ConvenientNote.Views;

public partial class KnowledgeMemoControl : UserControl
{
    private NotesViewModel? _viewModel;
    private bool _editing;
    private bool _dirty;
    private Task<bool>? _saving;
    private string? _deletedText;
    private int _renderVersion;
    private readonly HashSet<string> _collapsedHeadings = new(StringComparer.Ordinal);

    public KnowledgeMemoControl()
    {
        InitializeComponent();
        DataContextChanged += ContextChanged;
        Loaded += (_, _) => Render();
    }

    private void ContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null) PropertyChangedEventManager.RemoveHandler(_viewModel, ModelChanged, string.Empty);
        _viewModel = e.NewValue as NotesViewModel;
        if (_viewModel is not null) PropertyChangedEventManager.AddHandler(_viewModel, ModelChanged, string.Empty);
        _editing = _dirty = false;
        _deletedText = null;
        _collapsedHeadings.Clear();
        Render();
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotesViewModel.KnowledgeMemoText) && !_editing && _saving is null) Render();
    }

    private void Render()
    {
        if (_viewModel is null || _editing) return;
        var scroll = FindScrollViewer(Checklist);
        var offset = scroll?.VerticalOffset ?? 0;
        var version = ++_renderVersion;
        var rows = KnowledgeChecklist.Parse(_viewModel.KnowledgeMemoText);
        var visibleRows = new List<KnowledgeRow>();
        var collapsed = false;
        foreach (var row in rows)
        {
            if (row.IsHeading)
            {
                collapsed = _collapsedHeadings.Contains(row.HeadingKey);
                visibleRows.Add(row with { IsCollapsed = collapsed });
            }
            else if (!collapsed) visibleRows.Add(row);
        }
        Checklist.ItemsSource = visibleRows;
        ProgressText.Text = $"已掌握 {rows.Count(r => r.IsChecked)}/{rows.Count(r => r.HasCheck)}";
        StatusText.Text = _viewModel.KnowledgeMemoPersisted ? "已保存" : "初始清单";
        Checklist.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EditMemoButton.Content = rows.Count == 0 ? "新建" : "编辑";
        DeleteMemoButton.IsEnabled = rows.Count > 0;
        ReadActions.Visibility = Visibility.Visible;
        EditPanel.Visibility = Visibility.Collapsed;
        UndoDeleteButton.Visibility = _deletedText is null ? Visibility.Collapsed : Visibility.Visible;
        if (scroll is not null && offset > 0)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                if (version == _renderVersion) scroll.ScrollToVerticalOffset(offset);
            }));
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject node)
    {
        if (node is ScrollViewer scroll) return scroll;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
            if (FindScrollViewer(System.Windows.Media.VisualTreeHelper.GetChild(node, i)) is { } found) return found;
        return null;
    }

    private void Edit_Click(object sender, RoutedEventArgs e) => BeginEditing(_viewModel?.KnowledgeMemoText ?? "");

    private void Heading_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: KnowledgeRow { IsHeading: true } row }) return;
        if (!_collapsedHeadings.Add(row.HeadingKey)) _collapsedHeadings.Remove(row.HeadingKey);
        Render();
    }

    private void BeginEditing(string text)
    {
        _editing = true;
        MemoEditor.Text = text;
        _dirty = false;
        ReadActions.Visibility = Checklist.Visibility = EmptyText.Visibility = Visibility.Collapsed;
        EditPanel.Visibility = Visibility.Visible;
        StatusText.Text = "编辑中";
        MemoEditor.Focus();
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_editing) return;
        _dirty = true;
        StatusText.Text = "未保存";
    }

    public Task<bool> SaveChangesAsync()
    {
        if (_saving is not null) return _saving;
        if (_viewModel is null) return Task.FromResult(true);
        if (!_editing && _viewModel.KnowledgeMemoPersisted) return Task.FromResult(true);
        if (_editing && !_dirty && _viewModel.KnowledgeMemoPersisted)
        { _editing = false; Render(); return Task.FromResult(true); }
        return CommitAsync(_editing ? MemoEditor.Text : _viewModel.KnowledgeMemoText);
    }

    private async Task<bool> CommitAsync(string text)
    {
        if (_saving is not null) return await _saving;
        if (_viewModel is null) return false;
        var normalized = KnowledgeChecklist.Recount(text);
        StatusText.Text = "正在保存";
        IsEnabled = false;
        // Store the task before awaiting so close/export waits for the same write.
        _saving = _viewModel.SaveKnowledgeMemoAsync(normalized);
        bool saved;
        try { saved = await _saving; }
        finally { _saving = null; IsEnabled = true; }
        if (saved)
        {
            _deletedText = null;
            _editing = _dirty = false;
            Render();
        }
        else
        {
            BeginEditing(normalized);
            _dirty = true;
            StatusText.Text = "保存失败，请重试";
        }
        return saved;
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveChangesAsync();
    private void Cancel_Click(object sender, RoutedEventArgs e) { _editing = _dirty = false; Render(); }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: KnowledgeRow row } check && _viewModel is not null)
            await CommitAsync(KnowledgeChecklist.Toggle(_viewModel.KnowledgeMemoText, row.LineIndex, check.IsChecked == true));
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        var previous = _viewModel.KnowledgeMemoText;
        if (await CommitAsync("")) { _deletedText = previous; Render(); }
    }

    private async void UndoDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_deletedText is not { } text) return;
        if (await CommitAsync(text)) { _deletedText = null; Render(); }
    }

    private void InsertCheck_Click(object sender, RoutedEventArgs e)
    {
        var line = MemoEditor.GetLineIndexFromCharacterIndex(MemoEditor.CaretIndex);
        var start = MemoEditor.GetCharacterIndexFromLineIndex(line);
        var end = start + MemoEditor.GetLineLength(line);
        while (end > start && MemoEditor.Text[end - 1] is '\r' or '\n') end--;
        MemoEditor.Select(end, 0);
        MemoEditor.SelectedText = "　☐";
        MemoEditor.Focus();
    }

    private async void Control_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        { e.Handled = true; await SaveChangesAsync(); }
        else if (e.Key == Key.Escape && _editing)
        { e.Handled = true; _editing = _dirty = false; Render(); }
    }
}
