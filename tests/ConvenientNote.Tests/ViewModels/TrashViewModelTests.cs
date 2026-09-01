using System.IO;
using ConvenientNote.Application.Abstractions;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;
using ConvenientNote.Services;
using ConvenientNote.ViewModels;
using Xunit;

namespace ConvenientNote.Tests.ViewModels;

public sealed class TrashViewModelTests
{
    [Fact]
    public async Task RestoreReturnsDeletedNoteToNotesWorkspace()
    {
        var repository = new InMemoryRepository();
        var workspace = Workspace.Create("测试");
        var note = workspace.AddNote(
            TodoBoardKeys.Notes,
            "误删笔记",
            "正文",
            new NotePosition(10, 10),
            new NoteSize(280, 180),
            "#FFF8B8");
        note.MoveToTrash();
        await repository.SaveAsync(workspace);
        var service = new WorkspaceApplicationService(repository);
        var viewModel = new TrashViewModel(service, new NoteMediaService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        await viewModel.InitializeAsync();

        var deleted = Assert.Single(viewModel.DeletedNotes);
        await viewModel.RestoreAsync(deleted);

        Assert.Empty(viewModel.DeletedNotes);
        Assert.False(Assert.Single((await service.GetWorkspaceAsync(workspace.Id)).Notes).IsDeleted);
    }

    private sealed class InMemoryRepository : IWorkspaceRepository
    {
        private Workspace? _workspace;
        public Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Workspace>>(_workspace is null ? [] : [_workspace]);
        public Task<Workspace?> GetAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(_workspace?.Id == workspaceId ? _workspace : null);
        public Task SaveAsync(Workspace workspace, CancellationToken cancellationToken = default) { _workspace = workspace; return Task.CompletedTask; }
        public Task ReplaceAllAsync(Workspace workspace, CancellationToken cancellationToken = default) { _workspace = workspace; return Task.CompletedTask; }
        public Task DeleteAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
