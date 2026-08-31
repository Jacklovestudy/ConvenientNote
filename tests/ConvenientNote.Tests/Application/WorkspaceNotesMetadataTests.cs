using ConvenientNote.Application.Abstractions;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;
using Xunit;

namespace ConvenientNote.Tests.Application;

public sealed class WorkspaceNotesMetadataTests
{
    [Fact]
    public async Task ApplicationServicePersistsRichContentAndNoteFlags()
    {
        var repository = new InMemoryRepository();
        var workspace = Workspace.Create("笔记测试");
        var note = workspace.AddNote(
            TodoBoardKeys.Testing,
            "原始标题",
            "原始正文",
            new NotePosition(10, 20),
            new NoteSize(280, 180),
            "#FFF8B8");
        await repository.SaveAsync(workspace);
        var service = new WorkspaceApplicationService(repository);

        await service.UpdateRichNoteAsync(workspace.Id, note.Id, "{\"version\":1}", "新正文");
        await service.SetNotePinnedAsync(workspace.Id, note.Id, true);
        await service.SetNoteFavoriteAsync(workspace.Id, note.Id, true);
        await service.SetNoteTagsAsync(workspace.Id, note.Id, ["工作", "资料"]);

        var snapshot = Assert.Single((await service.GetWorkspaceAsync(workspace.Id)).Notes);
        Assert.Equal("{\"version\":1}", snapshot.RichContent);
        Assert.Equal("新正文", snapshot.Content);
        Assert.True(snapshot.IsPinned);
        Assert.True(snapshot.IsFavorite);
        Assert.Equal(["工作", "资料"], snapshot.Tags);
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

        public Task DeleteAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default)
        {
            _workspace = null;
            return Task.CompletedTask;
        }
    }
}
