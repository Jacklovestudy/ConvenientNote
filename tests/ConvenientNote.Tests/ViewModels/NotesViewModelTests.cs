using System.IO;
using ConvenientNote.Application.Abstractions;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;
using ConvenientNote.Services;
using ConvenientNote.ViewModels;
using Xunit;

namespace ConvenientNote.Tests.ViewModels;

public sealed class NotesViewModelTests
{
    [Fact]
    public async Task SearchFiltersTitleBodyAndTags()
    {
        var repository = await CreateRepositoryAsync();
        var viewModel = new NotesViewModel(
            new WorkspaceApplicationService(repository),
            new RichTextDocumentService(),
            new NoteMediaService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        await viewModel.InitializeAsync();

        viewModel.SearchText = "灵感";

        Assert.Equal("产品想法", Assert.Single(viewModel.FilteredNotes).Title);
    }

    [Fact]
    public async Task OpenNoteSwitchesFromWallToEditor()
    {
        var repository = await CreateRepositoryAsync();
        var viewModel = new NotesViewModel(
            new WorkspaceApplicationService(repository),
            new RichTextDocumentService(),
            new NoteMediaService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        await viewModel.InitializeAsync();
        var note = Assert.Single(viewModel.FilteredNotes, current => current.Title == "产品想法");

        viewModel.OpenNoteCommand.Execute(note);

        Assert.True(viewModel.IsEditorOpen);
        Assert.Same(note, viewModel.SelectedNote);
    }

    private static async Task<InMemoryRepository> CreateRepositoryAsync()
    {
        var repository = new InMemoryRepository();
        var workspace = Workspace.Create("测试");
        var first = workspace.AddNote(
            TodoBoardKeys.Testing,
            "产品想法",
            "记录功能",
            new NotePosition(20, 20),
            new NoteSize(280, 180),
            "#FFF8B8");
        first.SetTags(["灵感"]);
        workspace.AddNote(
            TodoBoardKeys.Testing,
            "会议记录",
            "周会内容",
            new NotePosition(320, 20),
            new NoteSize(280, 180),
            "#DCFCE7");
        await repository.SaveAsync(workspace);
        return repository;
    }

    private sealed class InMemoryRepository : IWorkspaceRepository
    {
        private Workspace? _workspace;

        public Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Workspace>>(_workspace is null ? [] : [_workspace]);

        public Task<Workspace?> GetAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_workspace?.Id == workspaceId ? _workspace : null);

        public Task SaveAsync(Workspace workspace, CancellationToken cancellationToken = default)
        {
            _workspace = workspace;
            return Task.CompletedTask;
        }

        public Task ReplaceActiveNotesAsync(WorkspaceId workspaceId, IReadOnlyCollection<Note> importedNotes, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
