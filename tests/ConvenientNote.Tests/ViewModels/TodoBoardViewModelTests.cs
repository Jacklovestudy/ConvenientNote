using System.Windows;
using ConvenientNote.Application.Abstractions;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;
using ConvenientNote.Services;
using ConvenientNote.ViewModels;
using Xunit;

namespace ConvenientNote.Tests.ViewModels;

public sealed class TodoBoardViewModelTests
{
    [Fact]
    public async Task DeleteTodoAsync_RemovesPersistedTodoAndRefreshesBoardState()
    {
        var repository = new InMemoryWorkspaceRepository();
        var workspace = Workspace.Create("Test workspace");
        workspace.AddNote(
            TodoBoardKeys.DayTodo,
            "Delete me",
            string.Empty,
            new NotePosition(32, 32),
            new NoteSize(260, 150),
            "#FFF8B8");
        await repository.SaveAsync(workspace);

        var workspaceApplicationService = new WorkspaceApplicationService(repository);
        var viewModel = new DayTodoViewModel(
            workspaceApplicationService,
            new OpenMeteoWeatherService());
        viewModel.OnNavigatedTo(null!);

        await WaitForAsync(() => viewModel.TodoItems.Count == 1);
        var todo = Assert.Single(viewModel.TodoItems);

        await viewModel.DeleteTodoAsync(todo);

        Assert.Empty(viewModel.TodoItems);
        Assert.Equal(Visibility.Visible, viewModel.EmptyStateVisibility);
        Assert.Equal(1800, viewModel.BoardWidth);
        Assert.Equal(1100, viewModel.BoardHeight);
        Assert.False(viewModel.CanArrangeTodos);
        Assert.Empty(repository.StoredWorkspace!.Notes);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationTokenSource.Token);
        }
    }

    private sealed class InMemoryWorkspaceRepository : IWorkspaceRepository
    {
        public Workspace? StoredWorkspace { get; private set; }

        public Task<IReadOnlyList<Workspace>> ListAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Workspace> workspaces = StoredWorkspace is null
                ? []
                : [StoredWorkspace];
            return Task.FromResult(workspaces);
        }

        public Task<Workspace?> GetAsync(
            WorkspaceId workspaceId,
            CancellationToken cancellationToken = default)
        {
            var workspace = StoredWorkspace?.Id == workspaceId
                ? StoredWorkspace
                : null;
            return Task.FromResult(workspace);
        }

        public Task SaveAsync(
            Workspace workspace,
            CancellationToken cancellationToken = default)
        {
            StoredWorkspace = workspace;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            WorkspaceId workspaceId,
            CancellationToken cancellationToken = default)
        {
            if (StoredWorkspace?.Id == workspaceId)
            {
                StoredWorkspace = null;
            }

            return Task.CompletedTask;
        }
    }
}
