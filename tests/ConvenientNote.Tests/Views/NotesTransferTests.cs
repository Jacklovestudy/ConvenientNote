using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using ConvenientNote.Application.Abstractions;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;
using ConvenientNote.Infrastructure.Persistence;
using ConvenientNote.Services;
using ConvenientNote.ViewModels;
using Microsoft.Data.Sqlite;
using ConvenientNote.Views;
using Prism.Mvvm;
using Prism.Navigation.Regions;
using System.Windows.Threading;
using Xunit;

namespace ConvenientNote.Tests.Views;

public sealed class NotesTransferTests
{
    private const string ImportWarning = "导入将覆盖当前未删除的全部笔记和图片，待办与回收站不受影响。此操作无法撤销。";

    [Fact]
    public void NotesToolbarOwnsOneCompactImportExportMenuImmediatelyBeforeNewNote()
    {
        RunSta(() =>
        {
            var view = LoadNotesMarkup();
            var trigger = Assert.IsType<MenuItem>(view.FindName("NotesTransferMenu"));
            var menu = Assert.IsType<Menu>(trigger.Parent);
            var newNoteButton = Assert.IsType<Button>(view.FindName("NewNoteButton"));
            var items = trigger.Items.Cast<MenuItem>().ToList();

            Assert.Equal("笔记导入导出", AutomationProperties.GetName(trigger));
            Assert.Equal(Visibility.Visible, trigger.Visibility);
            Assert.Equal(44, trigger.Width);
            Assert.Equal(44, trigger.Height);
            Assert.Collection(
                items,
                item =>
                {
                    Assert.Equal("导出笔记", item.Header);
                    Assert.Equal("导出笔记", AutomationProperties.GetName(item));
                },
                item =>
                {
                    Assert.Equal("导入笔记", item.Header);
                    Assert.Equal("导入笔记", AutomationProperties.GetName(item));
                });
            Assert.Equal(Grid.GetColumn(menu) + 1, Grid.GetColumn(newNoteButton));
            Assert.DoesNotContain(items, item => item.Width == 126);
        });
    }

    [Fact]
    public void NotesHeaderKeepsSearchAndFiltersInFlexibleLayoutAtMinimumWindowWidth()
    {
        RunSta(() =>
        {
            var view = LoadNotesMarkup();
            var search = Assert.IsType<Border>(view.FindName("NotesSearchBox"));
            var trigger = Assert.IsType<MenuItem>(view.FindName("NotesTransferMenu"));
            var header = Assert.IsType<Grid>(Assert.IsType<Menu>(trigger.Parent).Parent);

            Assert.Equal(GridUnitType.Star, header.ColumnDefinitions[1].Width.GridUnitType);
            Assert.Equal(44, search.Height);
            Assert.Equal(44, trigger.Width);
            Assert.DoesNotContain(
                header.Children.OfType<Button>(),
                button => button.Width == 126 &&
                          (Equals(button.Content, "导出笔记") || Equals(button.Content, "导入笔记")));
        });
    }

    [Fact]
    public void NotesHeaderMeasuresWithoutOverlapAt960Dips()
    {
        RunSta(() =>
        {
            var view = LoadNotesMarkup();
            view.Width = 960;
            view.Height = 720;
            view.Measure(new Size(960, 720));
            view.Arrange(new Rect(0, 0, 960, 720));
            view.UpdateLayout();

            var search = Assert.IsType<Border>(view.FindName("NotesSearchBox"));
            var trigger = Assert.IsType<MenuItem>(view.FindName("NotesTransferMenu"));
            var header = Assert.IsType<Grid>(Assert.IsType<Menu>(trigger.Parent).Parent);
            var orderedControls = header.Children
                .OfType<FrameworkElement>()
                .OrderBy(Grid.GetColumn)
                .ToList();
            var bounds = orderedControls
                .Select(control => new Rect(
                    control.TranslatePoint(new Point(0, 0), header),
                    new Size(control.ActualWidth, control.ActualHeight)))
                .ToList();

            Assert.Equal(960, view.ActualWidth);
            Assert.Equal(6, orderedControls.Count);
            Assert.All(orderedControls, control => Assert.True(control.ActualWidth > 0));
            Assert.True(search.ActualWidth >= 96);
            for (var index = 1; index < bounds.Count; index++)
            {
                Assert.True(
                    bounds[index].Left >= bounds[index - 1].Right - 0.5,
                    $"Columns {index - 1} and {index} overlap at 960 DIPs.");
            }

            Assert.True(bounds[^1].Right <= header.ActualWidth + 0.5);
        });
    }

    [Fact]
    public async Task NotesCountIncludesOnlyActiveNotes()
    {
        var workspace = Workspace.Create("测试");
        for (var index = 0; index < 5; index++)
        {
            workspace.AddNote(
                TodoBoardKeys.Notes,
                $"笔记 {index}",
                string.Empty,
                new NotePosition(10, 10),
                new NoteSize(280, 180),
                "#FFF8B8");
        }

        var deleted = workspace.AddNote(
            TodoBoardKeys.Notes,
            "回收站笔记",
            string.Empty,
            new NotePosition(10, 10),
            new NoteSize(280, 180),
            "#FFF8B8");
        deleted.MoveToTrash();
        workspace.AddNote(
            TodoBoardKeys.DayTodo,
            "待办",
            string.Empty,
            new NotePosition(10, 10),
            new NoteSize(280, 180),
            "#FFF8B8");
        var viewModel = new NotesViewModel(
            new WorkspaceApplicationService(new InMemoryWorkspaceRepository(workspace)),
            new RichTextDocumentService(),
            new NoteMediaService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));

        await viewModel.InitializeAsync();

        Assert.Equal("共 5 条笔记", viewModel.NotesSummary);
    }

    [Fact]
    public void ImportConfirmationUsesExactScopeCountAndSafeKeyboardDefault()
    {
        RunSta(() =>
        {
            var method = typeof(NotesView).GetMethod(
                "CreateNotesImportConfirmationContent",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var content = Assert.IsAssignableFrom<FrameworkElement>(method.Invoke(
                null,
                [new NotesBackupPreview(5, DateTimeOffset.UtcNow), (Action)(() => { }), (Action)(() => { })]));
            var text = FindDescendants<TextBlock>(content).Select(textBlock => textBlock.Text).ToList();
            var buttons = FindDescendants<Button>(content).ToList();
            var cancel = Assert.Single(buttons, button => Equals(button.Content, "取消"));
            var destructive = Assert.Single(buttons, button => Equals(button.Content, "覆盖并导入"));

            Assert.Contains(ImportWarning, text);
            Assert.Contains("共 5 条笔记", text);
            Assert.True(cancel.IsCancel);
            Assert.True(cancel.IsDefault);
            Assert.False(destructive.IsCancel);
            Assert.False(destructive.IsDefault);
        });
    }

    [Fact]
    public void NotesViewAndMainWindowUseConstructorInjectedTransferDependencies()
    {
        var notesConstructor = Assert.Single(typeof(NotesView).GetConstructors());
        Assert.Equal(
            [
                typeof(NotesBackupService),
                typeof(NotesBackupPackageStager),
                typeof(WorkspaceTransferRequestGate),
                typeof(IRegionManager)
            ],
            notesConstructor.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.True(typeof(NotesBackupPackageStager).IsPublic);

        var mainWindowConstructor = Assert.Single(typeof(MainWindow).GetConstructors());
        Assert.Equal(
            [typeof(WorkspaceTransferRequestGate)],
            mainWindowConstructor.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void ExportSaveFailureStopsBeforeFileSelectionAndShowsExactMessage()
    {
        RunSta(() =>
        {
            var temporaryDirectory = CreateTemporaryDirectory();
            try
            {
                var repository = new ControllableWorkspaceRepository(CreateWorkspaceWithActiveNote("未保存笔记"))
                {
                    FailSaves = true
                };
                var view = CreateOpenNotesView(repository, temporaryDirectory, out _);
                var selectionCount = 0;
                var messages = new List<string>();

                view.ExportNotesFromSelectionAsync(
                        () =>
                        {
                            selectionCount++;
                            return Path.Combine(temporaryDirectory, "should-not-exist.cnote");
                        },
                        (message, _) => messages.Add(message))
                    .GetAwaiter()
                    .GetResult();

                Assert.Equal(0, selectionCount);
                Assert.Equal(["保存失败，请重试"], messages);
                Assert.False(view.TransferInProgress);
            }
            finally
            {
                DeleteDirectory(temporaryDirectory);
            }
        });
    }

    [Fact]
    public void ExportSavesBeforeSelectionAndReportsTheActiveNoteCount()
    {
        RunSta(() =>
        {
            var temporaryDirectory = CreateTemporaryDirectory();
            try
            {
                var repository = new ControllableWorkspaceRepository(CreateWorkspaceWithActiveNote("导出笔记"));
                var view = CreateOpenNotesView(repository, temporaryDirectory, out _);
                var destination = Path.Combine(temporaryDirectory, "notes.CNOTE");
                var saveCountWhenSelected = -1;
                var messages = new List<string>();

                view.ExportNotesFromSelectionAsync(
                        () =>
                        {
                            saveCountWhenSelected = repository.SaveCount;
                            return destination;
                        },
                        (message, _) => messages.Add(message))
                    .GetAwaiter()
                    .GetResult();

                Assert.True(saveCountWhenSelected > 0);
                Assert.True(File.Exists(destination));
                Assert.Equal(["导出完成，共导出 1 条笔记"], messages);
                Assert.False(view.TransferInProgress);
            }
            finally
            {
                DeleteDirectory(temporaryDirectory);
            }
        });
    }

    [Fact]
    public void ExportSealsNotesMutationsUntilThePackageSnapshotIsComplete()
    {
        RunStaAsync(async () =>
        {
            var temporaryDirectory = CreateTemporaryDirectory();
            ControllableWorkspaceRepository? repository = null;
            try
            {
                repository = new ControllableWorkspaceRepository(CreateWorkspaceWithActiveNote("导出快照"));
                var view = CreateOpenNotesView(repository, temporaryDirectory, out var viewModel);
                var note = Assert.Single(viewModel.FilteredNotes);
                var mediaPath = Path.Combine(
                    temporaryDirectory,
                    "Media",
                    note.Id.Value.ToString("N"),
                    "snapshot.png");
                var destination = Path.Combine(temporaryDirectory, "stable.cnote");
                var messages = new List<string>();
                var surfaceDisabledAtSelection = false;
                var mutationSealedAtSelection = false;
                repository.BlockNextList = true;

                var exportTask = view.ExportNotesFromSelectionAsync(
                    () =>
                    {
                        surfaceDisabledAtSelection = !view.IsEnabled;
                        mutationSealedAtSelection = view.IsNotesMutationSealed;
                        Directory.CreateDirectory(Path.GetDirectoryName(mediaPath)!);
                        File.WriteAllBytes(mediaPath, [1, 3, 5, 7]);
                        return destination;
                    },
                    (message, _) => messages.Add(message));
                await repository.BlockedListStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

                var surfaceDisabledDuringExport = !view.IsEnabled;
                var mutationSealedDuringExport = view.IsNotesMutationSealed;
                var savesBeforeAttempt = repository.SaveCount;
                var laterDocument = new FlowDocument(new Paragraph(new Run("导出期间的新正文")));
                await viewModel.SaveDocumentAsync(laterDocument);
                var mutationRanDuringExport = repository.SaveCount != savesBeforeAttempt;
                repository.ReleaseBlockedList();
                await exportTask;

                using var archive = ZipFile.OpenRead(destination);
                var notesEntry = Assert.Single(archive.Entries, entry => entry.FullName == "notes.json");
                await using var notesStream = notesEntry.Open();
                var exportedDocument = await NotesBackupSerializer.ReadDocumentAsync(notesStream);
                var exportedNote = Assert.Single(exportedDocument.Notes);
                var mediaEntry = Assert.Single(
                    archive.Entries,
                    entry => entry.FullName == $"media/{note.Id.Value:N}/snapshot.png");
                await using var mediaStream = mediaEntry.Open();
                using var copiedMedia = new MemoryStream();
                await mediaStream.CopyToAsync(copiedMedia);

                Assert.True(surfaceDisabledDuringExport);
                Assert.True(mutationSealedDuringExport);
                Assert.True(surfaceDisabledAtSelection);
                Assert.True(mutationSealedAtSelection);
                Assert.False(mutationRanDuringExport);
                Assert.Equal("正文", exportedNote.Content);
                Assert.Equal([1, 3, 5, 7], copiedMedia.ToArray());
                Assert.True(view.IsEnabled);
                Assert.False(view.IsNotesMutationSealed);
                Assert.Equal(["导出完成，共导出 1 条笔记"], messages);

                await viewModel.SaveDocumentAsync(laterDocument);
                Assert.True(repository.SaveCount > savesBeforeAttempt);
                Assert.False(File.Exists(mediaPath));
            }
            finally
            {
                repository?.ReleaseBlockedList();
                DeleteDirectory(temporaryDirectory);
            }
        });
    }

    [Fact]
    public void ExportSelectionCancellationAlwaysResumesTheNotesSurface()
    {
        RunSta(() =>
        {
            var temporaryDirectory = CreateTemporaryDirectory();
            try
            {
                var repository = new ControllableWorkspaceRepository(CreateWorkspaceWithActiveNote("取消导出"));
                var view = CreateOpenNotesView(repository, temporaryDirectory, out _);
                var sealedDuringSelection = false;
                var messages = new List<string>();

                view.ExportNotesFromSelectionAsync(
                        () =>
                        {
                            sealedDuringSelection = view.IsNotesMutationSealed && !view.IsEnabled;
                            return null;
                        },
                        (message, _) => messages.Add(message))
                    .GetAwaiter()
                    .GetResult();

                Assert.True(sealedDuringSelection);
                Assert.Empty(messages);
                Assert.True(view.IsEnabled);
                Assert.False(view.IsNotesMutationSealed);
                Assert.False(view.TransferInProgress);
            }
            finally
            {
                DeleteDirectory(temporaryDirectory);
            }
        });
    }

    [Fact]
    public void ExportRejectsTheOriginalSelectedPathWhenItIsNotACnoteFile()
    {
        RunSta(() =>
        {
            var temporaryDirectory = CreateTemporaryDirectory();
            try
            {
                var repository = new ControllableWorkspaceRepository(CreateWorkspaceWithActiveNote("导出笔记"));
                var view = CreateOpenNotesView(repository, temporaryDirectory, out _);
                var destination = Path.Combine(temporaryDirectory, "notes.zip");
                var messages = new List<string>();

                view.ExportNotesFromSelectionAsync(
                        () => destination,
                        (message, _) => messages.Add(message))
                    .GetAwaiter()
                    .GetResult();

                Assert.False(File.Exists(destination));
                Assert.Equal(["导出失败，请重试"], messages);
                Assert.True(view.IsEnabled);
                Assert.False(view.IsNotesMutationSealed);
                Assert.False(view.TransferInProgress);
            }
            finally
            {
                DeleteDirectory(temporaryDirectory);
            }
        });
    }

    [Fact]
    public void PrecommitFailureResumesTheOldNotesParticipant()
    {
        RunSta(() =>
        {
            var temporaryDirectory = CreateTemporaryDirectory();
            try
            {
                var repository = new ControllableWorkspaceRepository(CreateWorkspaceWithActiveNote("旧笔记"));
                var view = CreateOpenNotesView(repository, temporaryDirectory, out _);
                var refreshCount = 0;

                Assert.Throws<InvalidOperationException>(() => view.ExecuteNotesReplacementAsync(
                        _ => Task.FromException<NotesBackupImportResult>(new InvalidOperationException("precommit")),
                        () => refreshCount++)
                    .GetAwaiter()
                    .GetResult());

                Assert.True(view.IsEnabled);
                Assert.False(view.IsNotesMutationSealed);
                Assert.Equal(0, refreshCount);
            }
            finally
            {
                DeleteDirectory(temporaryDirectory);
            }
        });
    }

    [Fact]
    public void PostcommitFailureRefreshesOnlyNotesAndKeepsTheOldParticipantSealed()
    {
        RunSta(() =>
        {
            var temporaryDirectory = CreateTemporaryDirectory();
            try
            {
                var repository = new ControllableWorkspaceRepository(CreateWorkspaceWithActiveNote("旧笔记"));
                var view = CreateOpenNotesView(repository, temporaryDirectory, out _);
                var refreshCount = 0;

                var error = Assert.Throws<NotesImportCommittedException>(() => view.ExecuteNotesReplacementAsync(
                        committed =>
                        {
                            committed();
                            return Task.FromException<NotesBackupImportResult>(new InvalidOperationException("postcommit"));
                        },
                        () => refreshCount++)
                    .GetAwaiter()
                    .GetResult());

                Assert.IsType<InvalidOperationException>(error.InnerException);
                Assert.False(view.IsEnabled);
                Assert.True(view.IsNotesMutationSealed);
                Assert.Equal(1, refreshCount);
            }
            finally
            {
                DeleteDirectory(temporaryDirectory);
            }
        });
    }

    [Fact]
    public void NotesRegionRefreshRemovesOnlyNotesAndRequestsNotesNavigation()
    {
        RunSta(() =>
        {
            var temporaryDirectory = CreateTemporaryDirectory();
            try
            {
                var repository = new ControllableWorkspaceRepository(CreateWorkspaceWithActiveNote("旧笔记"));
                var view = CreateOpenNotesView(repository, temporaryDirectory, out _);
                var region = new SingleActiveRegion();
                var todoView = new object();
                var scheduleView = new object();
                region.Add(view);
                region.Add(todoView);
                region.Add(scheduleView);
                string? navigationTarget = null;

                NotesView.RecreateNotesView(region, target => navigationTarget = target);

                Assert.DoesNotContain(view, region.Views);
                Assert.Contains(todoView, region.Views);
                Assert.Contains(scheduleView, region.Views);
                Assert.Equal(nameof(NotesView), navigationTarget);
            }
            finally
            {
                DeleteDirectory(temporaryDirectory);
            }
        });
    }

    [Fact]
    public void ImportStagesOnceAndUsesTheSameImmutablePackageForPreviewAndImport()
    {
        RunStaAsync(async () =>
        {
            var temporaryDirectory = CreateTemporaryDirectory();
            try
            {
                var selectedPackage = Path.Combine(temporaryDirectory, "selected.cnote");
                var sourceWorkspace = CreateWorkspaceWithActiveNote("原始备份笔记");
                var sourceMediaRoot = Path.Combine(temporaryDirectory, "SourceMedia");
                var sourceService = new NotesBackupService(
                    new WorkspaceApplicationService(new InMemoryWorkspaceRepository(sourceWorkspace)),
                    new NoteMediaService(sourceMediaRoot));
                await sourceService.ExportAsync(selectedPackage);

                var targetWorkspace = CreateWorkspaceWithActiveNote("旧活动笔记");
                targetWorkspace.AddNote(
                    TodoBoardKeys.DayTodo,
                    "保留待办",
                    string.Empty,
                    new NotePosition(20, 20),
                    new NoteSize(280, 180),
                    "#FFF8B8");
                var targetRepository = new SqliteWorkspaceRepository(
                    Path.Combine(temporaryDirectory, "workspace.db"));
                await SeedOnlyWorkspaceAsync(targetRepository, targetWorkspace);
                var view = CreateOpenNotesView(targetRepository, temporaryDirectory, out _);
                var confirmationCount = 0;
                var refreshCount = 0;
                var messages = new List<string>();

                await view.ImportNotesFromSelectionAsync(
                    () => selectedPackage,
                    preview =>
                    {
                        confirmationCount++;
                        Assert.Equal(1, preview.NoteCount);
                        File.WriteAllBytes(selectedPackage, [9, 8, 7]);
                        return true;
                    },
                    (message, _) => messages.Add(message),
                    () => refreshCount++);

                var restored = Assert.Single(await targetRepository.ListAsync());
                Assert.Contains(restored.Notes, note => note.Title == "原始备份笔记" && !note.IsDeleted);
                Assert.Contains(restored.Notes, note => note.Title == "保留待办");
                Assert.DoesNotContain(restored.Notes, note => note.Title == "旧活动笔记" && !note.IsDeleted);
                Assert.Equal(1, confirmationCount);
                Assert.Equal(1, refreshCount);
                Assert.Equal(["导入完成，共恢复 1 条笔记"], messages);
                Assert.False(view.TransferInProgress);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                DeleteDirectory(temporaryDirectory);
            }
        });
    }

    [Fact]
    public void ImportRejectsTheOriginalSelectedPathBeforeStagingWhenItIsNotACnoteFile()
    {
        RunStaAsync(async () =>
        {
            var temporaryDirectory = CreateTemporaryDirectory();
            try
            {
                var selectedPackage = Path.Combine(temporaryDirectory, "selected.zip");
                var sourceService = new NotesBackupService(
                    new WorkspaceApplicationService(
                        new InMemoryWorkspaceRepository(CreateWorkspaceWithActiveNote("不应导入"))),
                    new NoteMediaService(Path.Combine(temporaryDirectory, "SourceMedia")));
                await sourceService.ExportAsync(selectedPackage);
                var targetRepository = new SqliteWorkspaceRepository(
                    Path.Combine(temporaryDirectory, "workspace.db"));
                await SeedOnlyWorkspaceAsync(
                    targetRepository,
                    CreateWorkspaceWithActiveNote("保留笔记"));
                var view = CreateOpenNotesView(targetRepository, temporaryDirectory, out _);
                var confirmationCount = 0;
                var messages = new List<string>();

                await view.ImportNotesFromSelectionAsync(
                    () => selectedPackage,
                    _ =>
                    {
                        confirmationCount++;
                        return true;
                    },
                    (message, _) => messages.Add(message),
                    () => throw new InvalidOperationException("refresh must not run"));

                var stored = Assert.Single(await targetRepository.ListAsync());
                Assert.Contains(stored.Notes, note => note.Title == "保留笔记" && !note.IsDeleted);
                Assert.DoesNotContain(stored.Notes, note => note.Title == "不应导入" && !note.IsDeleted);
                Assert.Equal(0, confirmationCount);
                Assert.Equal([NotesBackupImportFailureMessages.GenericMessage], messages);
                Assert.False(view.TransferInProgress);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                DeleteDirectory(temporaryDirectory);
            }
        });
    }

    [Fact]
    public void ImportRefreshFailureReportsCommittedCountWithoutAdvisingAnotherImport()
    {
        RunStaAsync(async () =>
        {
            var temporaryDirectory = CreateTemporaryDirectory();
            try
            {
                var selectedPackage = Path.Combine(temporaryDirectory, "selected.cnote");
                var sourceService = new NotesBackupService(
                    new WorkspaceApplicationService(
                        new InMemoryWorkspaceRepository(CreateWorkspaceWithActiveNote("已导入笔记"))),
                    new NoteMediaService(Path.Combine(temporaryDirectory, "SourceMedia")));
                await sourceService.ExportAsync(selectedPackage);

                var targetRepository = new SqliteWorkspaceRepository(
                    Path.Combine(temporaryDirectory, "workspace.db"));
                await SeedOnlyWorkspaceAsync(
                    targetRepository,
                    CreateWorkspaceWithActiveNote("旧活动笔记"));
                var view = CreateOpenNotesView(targetRepository, temporaryDirectory, out _);
                var refreshAttempts = 0;
                var messages = new List<(string Text, MessageBoxImage Icon)>();

                await view.ImportNotesFromSelectionAsync(
                    () => selectedPackage,
                    preview =>
                    {
                        Assert.Equal(1, preview.NoteCount);
                        return true;
                    },
                    (message, icon) => messages.Add((message, icon)),
                    () =>
                    {
                        refreshAttempts++;
                        throw new IOException("simulated Notes region refresh failure");
                    });

                var stored = Assert.Single(await targetRepository.ListAsync());
                Assert.Contains(stored.Notes, note => note.Title == "已导入笔记" && !note.IsDeleted);
                Assert.DoesNotContain(stored.Notes, note => note.Title == "旧活动笔记" && !note.IsDeleted);
                Assert.Equal(1, refreshAttempts);
                var message = Assert.Single(messages);
                Assert.Equal(
                    "导入已完成，共恢复 1 条笔记，但刷新失败，请返回笔记后重试",
                    message.Text);
                Assert.Equal(MessageBoxImage.Warning, message.Icon);
                Assert.False(view.IsEnabled);
                Assert.True(view.IsNotesMutationSealed);
                Assert.False(view.TransferInProgress);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                DeleteDirectory(temporaryDirectory);
            }
        });
    }

    private static UserControl LoadNotesMarkup()
    {
        var markup = File.ReadAllText(FindSourceFile(Path.Combine("Views", "NotesView.xaml")));
        markup = markup.Replace(
            "xmlns:views=\"clr-namespace:ConvenientNote.Views\"",
            "xmlns:views=\"clr-namespace:ConvenientNote.Views;assembly=ConvenientNote\"");
        markup = Regex.Replace(
            markup,
            "\\s+(?:x:Class|prism:ViewModelLocator.AutoWireViewModel|Click)=\"[^\"]*\"",
            string.Empty);
        return Assert.IsType<UserControl>(XamlReader.Parse(markup));
    }

    private static NotesView CreateOpenNotesView(
        IWorkspaceRepository repository,
        string temporaryDirectory,
        out NotesViewModel viewModel)
    {
        var workspaceService = new WorkspaceApplicationService(repository);
        var mediaService = new NoteMediaService(Path.Combine(temporaryDirectory, "Media"));
        viewModel = new NotesViewModel(
            workspaceService,
            new RichTextDocumentService(),
            mediaService);
        var wiredViewModel = viewModel;
        ViewModelLocationProvider.SetDefaultViewModelFactory((_, viewModelType) =>
            viewModelType == typeof(NotesViewModel)
                ? wiredViewModel
                : Activator.CreateInstance(viewModelType)
                    ?? throw new InvalidOperationException($"Unable to create {viewModelType}."));
        var view = new NotesView(
            new NotesBackupService(workspaceService, mediaService),
            new NotesBackupPackageStager(),
            new WorkspaceTransferRequestGate(),
            new RegionManager());
        view.DataContext = viewModel;
        viewModel.InitializeAsync().GetAwaiter().GetResult();
        viewModel.OpenNoteCommand.Execute(Assert.Single(viewModel.FilteredNotes));
        return view;
    }

    private static Workspace CreateWorkspaceWithActiveNote(string title)
    {
        var workspace = Workspace.Create("测试");
        workspace.AddNote(
            TodoBoardKeys.Notes,
            title,
            "正文",
            new NotePosition(10, 10),
            new NoteSize(280, 180),
            "#FFF8B8");
        return workspace;
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ConvenientNote.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task SeedOnlyWorkspaceAsync(
        SqliteWorkspaceRepository repository,
        Workspace workspace)
    {
        foreach (var existingWorkspace in await repository.ListAsync())
        {
            await repository.DeleteAsync(existingWorkspace.Id);
        }

        await repository.SaveAsync(workspace);
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static string FindSourceFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate {relativePath} from the test output directory.");
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void RunStaAsync(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var task = action();
            task.ContinueWith(
                completedTask =>
                {
                    if (completedTask.IsFaulted)
                    {
                        failure = completedTask.Exception?.InnerException ?? completedTask.Exception;
                    }
                    else if (completedTask.IsCanceled)
                    {
                        failure = new TaskCanceledException(completedTask);
                    }

                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class InMemoryWorkspaceRepository(Workspace workspace) : IWorkspaceRepository
    {
        public Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Workspace>>([workspace]);

        public Task<Workspace?> GetAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Workspace?>(workspace.Id == workspaceId ? workspace : null);

        public Task SaveAsync(Workspace savedWorkspace, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ReplaceActiveNotesAsync(
            WorkspaceId workspaceId,
            IReadOnlyCollection<Note> importedNotes,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ControllableWorkspaceRepository(Workspace workspace) : IWorkspaceRepository
    {
        private readonly TaskCompletionSource _blockedListStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseBlockedList =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool FailSaves { get; set; }
        public bool BlockNextList { get; set; }
        public int SaveCount { get; private set; }
        public TaskCompletionSource BlockedListStarted => _blockedListStarted;

        public async Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken = default)
        {
            if (BlockNextList)
            {
                BlockNextList = false;
                _blockedListStarted.TrySetResult();
                await _releaseBlockedList.Task.WaitAsync(cancellationToken);
            }

            return [workspace];
        }

        public void ReleaseBlockedList() => _releaseBlockedList.TrySetResult();

        public Task<Workspace?> GetAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Workspace?>(workspace.Id == workspaceId ? workspace : null);

        public Task SaveAsync(Workspace savedWorkspace, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (FailSaves)
            {
                throw new IOException("simulated save failure");
            }

            workspace = savedWorkspace;
            return Task.CompletedTask;
        }

        public Task ReplaceActiveNotesAsync(
            WorkspaceId workspaceId,
            IReadOnlyCollection<Note> importedNotes,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
