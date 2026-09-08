using System.IO;
using System.Windows;
using System.Windows.Controls;
using ConvenientNote.Services;
using ConvenientNote.ViewModels;
using Microsoft.Win32;
using Prism.Navigation.Regions;

namespace ConvenientNote.Views;

public partial class NotesView : UserControl, ConvenientNote.UI.Common.IPageLifecycle
{
    private readonly INotesBackupService _notesBackupService;
    private readonly INotesBackupPackageStager _packageStager;
    private readonly WorkspaceTransferRequestGate _transferGate;
    private readonly IRegionManager _regionManager;
    private readonly NotesReplacementOperationGate _mutationGate = new();

    public NotesView(
        INotesBackupService notesBackupService,
        INotesBackupPackageStager packageStager,
        WorkspaceTransferRequestGate transferGate,
        IRegionManager regionManager)
    {
        _notesBackupService = notesBackupService;
        _packageStager = packageStager;
        _transferGate = transferGate;
        _regionManager = regionManager;
        DataContextChanged += NotesView_DataContextChanged;
        InitializeComponent();
        AttachNotesReplacementGate(DataContext);
    }

    public async Task<bool> FlushAsync() => await KnowledgeMemo.SaveChangesAsync() && await EditorControl.SaveNowAsync();

    public bool RequiresNavigationPreparation => IsEditorOpen;
    public Task<bool> PrepareToLeaveAsync() => ReturnToWallAsync();

    public bool IsEditorOpen => DataContext is NotesViewModel { IsEditorOpen: true };

    public Task<bool> ReturnToWallAsync() => EditorControl.ReturnToWallAsync();

    internal bool TransferInProgress => _transferGate.IsInProgress;

    internal bool IsNotesMutationSealed => _mutationGate.IsPreparing;

    private async void ExportNotesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ExportNotesFromSelectionAsync(SelectExportPath, ShowSimpleMessage);
        }
        catch
        {
            // The transfer method handles and reports every expected failure.
        }
    }

    private async void ImportNotesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ImportNotesFromSelectionAsync(
                SelectImportPath,
                ConfirmNotesImport,
                ShowSimpleMessage);
        }
        catch
        {
            // The transfer method handles and reports every expected failure.
        }
    }

    internal async Task ExportNotesFromSelectionAsync(
        Func<string?> selectDestination,
        Action<string, MessageBoxImage> showMessage)
    {
        ArgumentNullException.ThrowIfNull(selectDestination);
        ArgumentNullException.ThrowIfNull(showMessage);

        if (!_transferGate.TryBegin())
        {
            return;
        }

        var notesSealed = false;
        try
        {
            if (!await FlushAsync())
            {
                showMessage("保存失败，请重试", MessageBoxImage.Error);
                return;
            }

            var drain = PrepareForNotesReplacementAsync();
            notesSealed = true;
            await drain;
            var destinationPath = selectDestination();
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                return;
            }

            if (!HasNotesBackupExtension(destinationPath))
            {
                throw new InvalidDataException("Notes exports require a .cnote destination.");
            }

            var result = await _notesBackupService.ExportAsync(destinationPath);
            showMessage($"导出完成，共导出 {result.NoteCount} 条笔记", MessageBoxImage.Information);
        }
        catch
        {
            showMessage("导出失败，请重试", MessageBoxImage.Error);
        }
        finally
        {
            if (notesSealed)
            {
                ResumeAfterNotesReplacementFailure();
            }

            _transferGate.Complete();
        }
    }

    internal async Task ImportNotesFromSelectionAsync(
        Func<string?> selectSource,
        Func<NotesBackupPreview, bool> confirmImport,
        Action<string, MessageBoxImage> showMessage,
        Action? refreshNotesView = null)
    {
        ArgumentNullException.ThrowIfNull(selectSource);
        ArgumentNullException.ThrowIfNull(confirmImport);
        ArgumentNullException.ThrowIfNull(showMessage);

        if (!_transferGate.TryBegin())
        {
            return;
        }

        NotesBackupPreview? preview = null;
        try
        {
            var sourcePath = selectSource();
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return;
            }

            if (!HasNotesBackupExtension(sourcePath))
            {
                throw new InvalidDataException("Notes imports require a .cnote source.");
            }

            await using var packageSnapshot = await _packageStager.StageAsync(sourcePath);
            preview = await _notesBackupService.InspectAsync(packageSnapshot.PackagePath);
            if (!confirmImport(preview))
            {
                return;
            }

            var result = await ExecuteNotesReplacementAsync(
                committed => _notesBackupService.ImportOverwriteAsync(
                    packageSnapshot.PackagePath,
                    committed),
                refreshNotesView ?? RecreateNotesView);
            showMessage($"导入完成，共恢复 {result.NoteCount} 条笔记", MessageBoxImage.Information);
        }
        catch (NotesImportCommittedException) when (preview is not null)
        {
            showMessage(
                $"导入已完成，共恢复 {preview.NoteCount} 条笔记，但刷新失败，请返回笔记后重试",
                MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            showMessage(NotesBackupImportFailureMessages.GetMessage(exception), MessageBoxImage.Error);
        }
        finally
        {
            _transferGate.Complete();
        }
    }

    internal async Task<NotesBackupImportResult> ExecuteNotesReplacementAsync(
        Func<Action, Task<NotesBackupImportResult>> importNotesAsync,
        Action recreateNotesView)
    {
        ArgumentNullException.ThrowIfNull(importNotesAsync);
        ArgumentNullException.ThrowIfNull(recreateNotesView);

        await PrepareForNotesReplacementAsync();
        var replacementCommitted = false;
        var refreshAttempted = false;

        void MarkCommitted() => replacementCommitted = true;
        void RefreshOnce()
        {
            if (refreshAttempted)
            {
                return;
            }

            refreshAttempted = true;
            recreateNotesView();
        }

        try
        {
            var result = await importNotesAsync(MarkCommitted);
            replacementCommitted = true;
            RefreshOnce();
            return result;
        }
        catch (Exception exception)
        {
            if (replacementCommitted)
            {
                Exception committedException = exception;
                try
                {
                    RefreshOnce();
                }
                catch (Exception refreshException)
                {
                    committedException = new AggregateException(exception, refreshException);
                }

                throw new NotesImportCommittedException(committedException);
            }

            ResumeAfterNotesReplacementFailure();
            throw;
        }
    }

    internal async Task PrepareForNotesReplacementAsync()
    {
        var editorDrain = EditorControl.PrepareForNotesReplacementAsync();
        var viewModelDrain = _mutationGate.PrepareAndDrainAsync();
        await Task.WhenAll(editorDrain, viewModelDrain);
        IsEnabled = false;
    }

    internal void ResumeAfterNotesReplacementFailure()
    {
        EditorControl.ResumeAfterNotesReplacementFailure();
        _mutationGate.Resume();
        IsEnabled = true;
    }

    internal static void RecreateNotesView(IRegion region, Action<string> requestNavigate)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(requestNavigate);

        foreach (var notesView in region.Views.OfType<NotesView>().ToList())
        {
            region.Remove(notesView);
        }

        requestNavigate(nameof(NotesView));
    }

    private void RecreateNotesView()
    {
        var region = _regionManager.Regions[ConvenientNote.UI.Common.ApplicationRegions.Main];
        RecreateNotesView(
            region,
            target => _regionManager.RequestNavigate(ConvenientNote.UI.Common.ApplicationRegions.Main, target));
    }

    private string? SelectExportPath()
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出笔记",
            Filter = "Convenient Note 笔记备份 (*.cnote)|*.cnote",
            DefaultExt = ".cnote",
            AddExtension = true,
            FileName = $"ConvenientNote-笔记-{DateTime.Now:yyyy-MM-dd}.cnote"
        };
        return dialog.ShowDialog(Window.GetWindow(this)) == true
            ? dialog.FileName
            : null;
    }

    private static bool HasNotesBackupExtension(string path) =>
        string.Equals(Path.GetExtension(path), ".cnote", StringComparison.OrdinalIgnoreCase);

    private string? SelectImportPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入笔记",
            Filter = "Convenient Note 笔记备份 (*.cnote)|*.cnote",
            DefaultExt = ".cnote",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog(Window.GetWindow(this)) == true
            ? dialog.FileName
            : null;
    }

    private bool ConfirmNotesImport(NotesBackupPreview preview)
    {
        var confirmed = false;
        var dialog = new ConvenientNote.UI.Common.MaterialDialogWindow("确认导入", Window.GetWindow(this));

        dialog.DialogContent = CreateNotesImportConfirmationContent(
            preview,
            () =>
            {
                confirmed = true;
                dialog.DialogResult = true;
            },
            () => dialog.DialogResult = false);
        dialog.ShowDialog();
        return confirmed;
    }

    private static UIElement CreateNotesImportConfirmationContent(
        NotesBackupPreview preview,
        Action confirm,
        Action cancel)
    {
        var container = new Grid();
        container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        container.Children.Add(new TextBlock
        {
            Text = "导入将覆盖当前未删除的全部笔记和图片，包含右侧知识点便签。待办与回收站不受影响。此操作无法撤销。",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        var summary = new TextBlock
        {
            Text = $"共 {preview.NoteCount} 条笔记",
            Margin = new Thickness(0, 10, 0, 18),
            Foreground = System.Windows.Media.Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(summary, 1);
        container.Children.Add(summary);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancelButton = new Button
        {
            Content = "取消",
            Padding = new Thickness(12, 6, 12, 6),
            IsCancel = true,
            IsDefault = true
        };
        cancelButton.Click += (_, _) => cancel();
        cancelButton.SetResourceReference(StyleProperty, "MaterialDesignFlatButton");
        var confirmButton = new Button
        {
            Content = "覆盖并导入",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(12, 6, 12, 6),
            IsDefault = false
        };
        confirmButton.Click += (_, _) => confirm();
        confirmButton.SetResourceReference(StyleProperty, "MaterialDesignRaisedButton");
        actions.Children.Add(cancelButton);
        actions.Children.Add(confirmButton);
        Grid.SetRow(actions, 2);
        container.Children.Add(actions);
        return container;
    }

    private void NotesView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        AttachNotesReplacementGate(e.NewValue);
    }

    private void AttachNotesReplacementGate(object? dataContext)
    {
        if (dataContext is NotesViewModel viewModel)
        {
            viewModel.SetNotesReplacementOperationGate(_mutationGate);
        }
    }

    private void ShowSimpleMessage(string message, MessageBoxImage icon)
    {
        var title = icon switch
        {
            MessageBoxImage.Error => "操作失败",
            MessageBoxImage.Warning => "请留意",
            _ => "操作完成"
        };
        ConvenientNote.UI.Common.MaterialDialogWindow.Inform(Window.GetWindow(this), title, message);
    }
}

internal sealed class NotesImportCommittedException(Exception innerException)
    : Exception("Notes import committed, but its post-commit refresh did not complete.", innerException);
