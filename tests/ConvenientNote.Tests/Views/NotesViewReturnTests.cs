using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using ConvenientNote.Application.Abstractions;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;
using ConvenientNote.Services;
using ConvenientNote.ViewModels;
using ConvenientNote.Views;
using Xunit;

namespace ConvenientNote.Tests.Views;

public sealed class NotesViewReturnTests
{
    [Fact]
    public void ReturnToWallClosesEditorAfterSuccessfulSave()
    {
        RunSta(() =>
        {
            var (view, viewModel, _) = CreateOpenEditor();

            var returned = view.ReturnToWallAsync().GetAwaiter().GetResult();

            Assert.True(returned);
            Assert.False(viewModel.IsEditorOpen);
        });
    }

    [Fact]
    public void ReturnToWallStaysInEditorWhenSaveFails()
    {
        RunSta(() =>
        {
            var (view, viewModel, repository) = CreateOpenEditor();
            repository.FailSaves = true;

            var returned = view.ReturnToWallAsync().GetAwaiter().GetResult();

            Assert.False(returned);
            Assert.True(viewModel.IsEditorOpen);
        });
    }

    private static (RichNoteEditorControl View, NotesViewModel ViewModel, ControllableRepository Repository) CreateOpenEditor()
    {
        var repository = new ControllableRepository();
        var workspace = Workspace.Create("测试");
        workspace.AddNote(
            TodoBoardKeys.Notes,
            "测试笔记",
            "正文",
            new NotePosition(20, 20),
            new NoteSize(280, 180),
            "#FFF8B8");
        repository.Workspace = workspace;
        var viewModel = new NotesViewModel(
            new WorkspaceApplicationService(repository),
            new RichTextDocumentService(),
            new NoteMediaService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        viewModel.InitializeAsync().GetAwaiter().GetResult();
        viewModel.OpenNoteCommand.Execute(Assert.Single(viewModel.FilteredNotes));
        var view = new RichNoteEditorControl { DataContext = viewModel };
        return (view, viewModel, repository);
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

    private sealed class ControllableRepository : IWorkspaceRepository
    {
        public Workspace? Workspace { get; set; }
        public bool FailSaves { get; set; }

        public Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Workspace>>(Workspace is null ? [] : [Workspace]);

        public Task<Workspace?> GetAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Workspace?.Id == workspaceId ? Workspace : null);

        public Task SaveAsync(Workspace workspace, CancellationToken cancellationToken = default)
        {
            if (FailSaves)
            {
                throw new IOException("模拟保存失败");
            }

            Workspace = workspace;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
