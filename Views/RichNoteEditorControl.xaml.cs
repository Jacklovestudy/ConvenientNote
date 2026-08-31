using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConvenientNote.ViewModels;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;

namespace ConvenientNote.Views;

public partial class RichNoteEditorControl : UserControl
{
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private readonly DispatcherTimer _saveFeedbackTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private readonly ManualSaveRequestGate _manualSaveRequestGate = new();
    private NotesViewModel? _viewModel;
    private bool _isLoading;
    private bool _isUpdatingFontSize;
    private int _colorIndex;
    private int _saveFeedbackVersion;
    private static readonly Brush[] TextColors =
    [
        Brushes.Black,
        new SolidColorBrush(Color.FromRgb(63, 81, 181)),
        new SolidColorBrush(Color.FromRgb(180, 35, 24)),
        new SolidColorBrush(Color.FromRgb(4, 120, 87))
    ];

    public RichNoteEditorControl()
    {
        InitializeComponent();
        _saveTimer.Tick += SaveTimer_Tick;
        _saveFeedbackTimer.Tick += SaveFeedbackTimer_Tick;
        DataContextChanged += RichNoteEditorControl_DataContextChanged;
    }

    private void RichNoteEditorControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.SelectedNoteChanged -= ViewModel_SelectedNoteChanged;
            _viewModel.SaveRequested -= ViewModel_SaveRequested;
        }
        _viewModel = e.NewValue as NotesViewModel;
        if (_viewModel is not null)
        {
            _viewModel.SelectedNoteChanged += ViewModel_SelectedNoteChanged;
            _viewModel.SaveRequested += ViewModel_SaveRequested;
        }
        LoadSelectedNote();
    }

    private void ViewModel_SelectedNoteChanged(object? sender, EventArgs e) => LoadSelectedNote();

    private async void ViewModel_SaveRequested(object? sender, EventArgs e) => await SaveNowAsync();

    private void LoadSelectedNote()
    {
        if (_viewModel?.SelectedNote is not { } note)
        {
            return;
        }
        _isLoading = true;
        _saveTimer.Stop();
        Editor.Document = _viewModel.DocumentService.Load(note.RichContent, note.Content);
        TagsTextBox.Text = string.Join(", ", note.Tags);
        NotebookComboBox.SelectedItem = _viewModel.AvailableNotebooks.FirstOrDefault(option => option.Id == note.NotebookId);
        _isLoading = false;
        Editor.Focus();
    }

    private void EditorContentChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _viewModel?.SelectedNote is null)
        {
            return;
        }
        ScheduleSave();
    }

    private void TagsTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _viewModel?.SelectedNote is not { } note)
        {
            return;
        }
        note.Tags = TagsTextBox.Text
            .Split([',', '，'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();
        ScheduleSave();
    }

    private void NotebookComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || _viewModel?.SelectedNote is not { } note || NotebookComboBox.SelectedItem is not NotebookOption option)
        {
            return;
        }
        note.NotebookId = option.Id;
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private async void SaveTimer_Tick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        await SaveNowAsync();
    }

    public async Task<bool> SaveNowAsync()
    {
        if (_isLoading || _viewModel?.SelectedNote is null)
        {
            return true;
        }
        _saveTimer.Stop();
        try
        {
            await _viewModel.SaveDocumentAsync(Editor.Document);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal void ShowSaveFeedback(bool saved)
    {
        _saveFeedbackVersion++;
        _saveFeedbackTimer.Stop();
        SaveFeedbackBorder.BeginAnimation(OpacityProperty, null);
        SaveFeedbackBorder.Background = new SolidColorBrush(saved
            ? Color.FromRgb(23, 32, 51)
            : Color.FromRgb(180, 35, 24));
        SaveFeedbackIcon.Kind = saved ? PackIconKind.CheckCircleOutline : PackIconKind.AlertCircleOutline;
        var message = saved ? "已保存" : "保存失败，请重试";
        SaveFeedbackText.Text = message;
        AutomationProperties.SetName(SaveFeedbackText, message);
        SaveFeedbackBorder.Opacity = 1;
        SaveFeedbackBorder.Visibility = Visibility.Visible;
        if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
        {
            var peer = UIElementAutomationPeer.FromElement(SaveFeedbackText)
                ?? UIElementAutomationPeer.CreatePeerForElement(SaveFeedbackText);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        _saveFeedbackTimer.Start();
    }

    private void SaveFeedbackTimer_Tick(object? sender, EventArgs e)
    {
        _saveFeedbackTimer.Stop();
        var version = _saveFeedbackVersion;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        fade.Completed += (_, _) =>
        {
            if (version != _saveFeedbackVersion)
            {
                return;
            }

            SaveFeedbackBorder.Visibility = Visibility.Collapsed;
            SaveFeedbackBorder.BeginAnimation(OpacityProperty, null);
            SaveFeedbackBorder.Opacity = 0;
        };
        SaveFeedbackBorder.BeginAnimation(OpacityProperty, fade);
    }

    private async void RichNoteEditorControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.S || Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        e.Handled = true;
        if (!_manualSaveRequestGate.TryBegin(e.IsRepeat))
        {
            return;
        }

        try
        {
            ShowSaveFeedback(await SaveNowAsync());
        }
        finally
        {
            _manualSaveRequestGate.Complete();
        }
    }

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        await SaveNowAsync();
        _viewModel?.CloseEditorCommand.Execute();
    }

    private async void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            await SaveNowAsync();
            _viewModel?.CloseEditorCommand.Execute();
            e.Handled = true;
        }
        else if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Clipboard.ContainsImage())
        {
            await InsertClipboardImageAsync();
            e.Handled = true;
        }
    }

    private void ParagraphStyleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ParagraphStyleComboBox.SelectedItem is not ComboBoxItem item || !double.TryParse(item.Tag?.ToString(), out var size))
        {
            return;
        }
        Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
        RefreshFontSizeSelection();
        Editor.Focus();
    }

    private void FontSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _isUpdatingFontSize || FontSizeComboBox.SelectedItem is not ComboBoxItem item ||
            !double.TryParse(item.Tag?.ToString(), out var size))
        {
            return;
        }

        Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
        Editor.Focus();
    }

    private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        RefreshFontSizeSelection();
    }

    private void RefreshFontSizeSelection()
    {
        if (!IsLoaded || _isUpdatingFontSize)
        {
            return;
        }

        var value = Editor.Selection.GetPropertyValue(TextElement.FontSizeProperty);
        _isUpdatingFontSize = true;
        try
        {
            FontSizeComboBox.SelectedItem = value is double size
                ? FontSizeComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
                    double.TryParse(item.Tag?.ToString(), out var option) && Math.Abs(option - size) < 0.01)
                : null;
        }
        finally
        {
            _isUpdatingFontSize = false;
        }
    }

    private void StrikethroughButton_Click(object sender, RoutedEventArgs e)
    {
        Editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Strikethrough);
        Editor.Focus();
    }

    private void TextColorButton_Click(object sender, RoutedEventArgs e)
    {
        _colorIndex = (_colorIndex + 1) % TextColors.Length;
        Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, TextColors[_colorIndex]);
        Editor.Focus();
    }

    private async void InsertImageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "插入图片",
            Filter = "图片|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp"
        };
        if (dialog.ShowDialog() == true)
        {
            await InsertImageAsync(dialog.FileName);
        }
    }

    private async void Editor_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var file in files.Where(IsSupportedImage))
            {
                await InsertImageAsync(file);
            }
            e.Handled = true;
        }
    }

    private async Task InsertClipboardImageAsync()
    {
        var bitmap = Clipboard.GetImage();
        if (bitmap is null)
        {
            return;
        }
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"ConvenientNote-{Guid.NewGuid():N}.png");
        try
        {
            await using var stream = File.Create(temporaryPath);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
            await stream.FlushAsync();
            await InsertImageAsync(temporaryPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private async Task InsertImageAsync(string path)
    {
        if (_viewModel?.SelectedNote is not { } note)
        {
            return;
        }
        var relative = await _viewModel.MediaService.ImportAsync(note.Id, path);
        var absolute = _viewModel.MediaService.GetAbsolutePath(relative);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(absolute, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        var width = Math.Min(640, bitmap.PixelWidth > 0 ? bitmap.PixelWidth : 480);
        var image = new Image { Source = bitmap, Tag = relative, Width = width, Stretch = Stretch.Uniform, Margin = new Thickness(0, 10, 0, 10) };
        _ = new InlineUIContainer(image, Editor.CaretPosition);
        Editor.CaretPosition = image.Parent is InlineUIContainer container ? container.ElementEnd : Editor.CaretPosition;
        ScheduleSave();
    }

    private static bool IsSupportedImage(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }
}
