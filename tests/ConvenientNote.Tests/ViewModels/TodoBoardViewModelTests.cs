using System.IO;
using System.Reflection;
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
    public async Task DateSelectionFiltersScheduledTasksWhileInboxKeepsUnscheduledTasks()
    {
        var repository = new InMemoryWorkspaceRepository();
        var service = new WorkspaceApplicationService(repository);
        var workspace = await service.GetOrCreateDefaultWorkspaceAsync();
        var today = await service.CreateScheduledTodoAsync(workspace.Id, "今天", DateTime.Today);
        var tomorrow = await service.CreateScheduledTodoAsync(workspace.Id, "明天", DateTime.Today.AddDays(1));
        await service.CreateNoteAsync(workspace.Id, 0, 0, "未安排");
        var day = new DayTodoViewModel(service, new OpenMeteoWeatherService());
        await NavigateToWorkspaceAsync(day, 1);
        Assert.Equal(today.Id, Assert.Single(day.TodoItems).Id);
        day.SelectDateCommand.Execute(new DateTabViewModel(DateTime.Today.AddDays(1), false, true));
        Assert.Equal(tomorrow.Id, Assert.Single(day.TodoItems).Id);
        var inbox = new InboxViewModel(service, new OpenMeteoWeatherService());
        await NavigateToWorkspaceAsync(inbox, 3);
        Assert.Equal(3, inbox.TodoItems.Count);
    }

    [Fact]
    public async Task DeleteTodoAsync_RemovesPersistedTodoAndRefreshesBoardState()
    {
        var (viewModel, repository, todo) = await CreateLoadedViewModelAsync();

        await viewModel.DeleteTodoAsync(todo);

        Assert.Empty(viewModel.TodoItems);
        Assert.Equal(Visibility.Visible, viewModel.EmptyStateVisibility);
        Assert.Equal(1800, viewModel.BoardWidth);
        Assert.Equal(1100, viewModel.BoardHeight);
        Assert.False(viewModel.CanArrangeTodos);
        Assert.Empty(repository.StoredWorkspace!.Notes);
    }

    [Fact]
    public async Task CommitTodoTitleAsync_AfterSuccessfulDeletion_DoesNotRestoreOrThrowForStaleTodo()
    {
        var (viewModel, repository, todo) = await CreateLoadedViewModelAsync();
        todo.Title = "Stale title";

        await viewModel.DeleteTodoAsync(todo);
        var exception = await Record.ExceptionAsync(() => viewModel.CommitTodoTitleAsync(todo));

        Assert.Null(exception);
        Assert.Empty(repository.StoredWorkspace!.Notes);
    }

    [Fact]
    public async Task CommitTodoContentAsync_AfterSuccessfulDeletion_DoesNotRestoreOrThrowForStaleTodo()
    {
        var (viewModel, repository, todo) = await CreateLoadedViewModelAsync();
        todo.Content = "Stale content";

        await viewModel.DeleteTodoAsync(todo);
        var exception = await Record.ExceptionAsync(() => viewModel.CommitTodoContentAsync(todo));

        Assert.Null(exception);
        Assert.Empty(repository.StoredWorkspace!.Notes);
    }

    [Fact]
    public async Task DeleteTodoAsync_WhenPersistenceFails_KeepsTodoVisibleAndEditable()
    {
        var (viewModel, repository, todo) = await CreateLoadedViewModelAsync();
        repository.FailNextSave = true;
        todo.Title = "Edited after failed deletion";

        await viewModel.DeleteTodoAsync(todo);
        await viewModel.CommitTodoTitleAsync(todo);

        Assert.Same(todo, Assert.Single(viewModel.TodoItems));
        var persistedTodo = Assert.Single(repository.StoredWorkspace!.Notes);
        Assert.Equal("Edited after failed deletion", persistedTodo.Title);
    }

    [Fact]
    public async Task DeleteTodoAsync_WhenDeletionAlreadySucceeded_DoesNotAttemptDeletionAgain()
    {
        var (viewModel, repository, todo) = await CreateLoadedViewModelAsync();

        await viewModel.DeleteTodoAsync(todo);
        var getCallsAfterDeletion = repository.GetAsyncCallCount;
        await viewModel.DeleteTodoAsync(todo);

        Assert.Equal(getCallsAfterDeletion, repository.GetAsyncCallCount);
        Assert.Empty(repository.StoredWorkspace!.Notes);
    }

    private static async Task<(DayTodoViewModel ViewModel, InMemoryWorkspaceRepository Repository, CanvasTodoViewModel Todo)>
        CreateLoadedViewModelAsync()
    {
        var repository = new InMemoryWorkspaceRepository();
        var workspace = Workspace.Create("Test workspace");
        var scheduled = workspace.AddNote(
            TodoBoardKeys.DayTodo,
            "Delete me",
            "Original content",
            new NotePosition(32, 32),
            new NoteSize(260, 150),
            "#FFF8B8");
        workspace.SetNotePlannedDate(scheduled.Id, DateTime.Today);
        await repository.SaveAsync(workspace);

        var workspaceApplicationService = new WorkspaceApplicationService(repository);
        var viewModel = new DayTodoViewModel(
            workspaceApplicationService,
            new OpenMeteoWeatherService());

        await NavigateToWorkspaceAsync(viewModel, expectedTodoCount: 1);

        return (viewModel, repository, Assert.Single(viewModel.TodoItems));
    }

    private static async Task NavigateToWorkspaceAsync(
        TodoBoardViewModel viewModel,
        int expectedTodoCount)
    {
        var hasLoadedWeatherField = typeof(TodoBoardViewModel).GetField(
            "_hasLoadedWeather",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(hasLoadedWeatherField);
        hasLoadedWeatherField.SetValue(viewModel, true);

        viewModel.OnNavigatedTo(null!);
        await WaitForAsync(() => viewModel.TodoItems.Count == expectedTodoCount);
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

        public bool FailNextSave { get; set; }

        public int GetAsyncCallCount { get; private set; }

        public Task<IReadOnlyList<Workspace>> ListAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Workspace> workspaces = StoredWorkspace is null
                ? []
                : [Clone(StoredWorkspace)];
            return Task.FromResult(workspaces);
        }

        public Task<Workspace?> GetAsync(
            WorkspaceId workspaceId,
            CancellationToken cancellationToken = default)
        {
            GetAsyncCallCount++;
            var workspace = StoredWorkspace?.Id == workspaceId
                ? Clone(StoredWorkspace)
                : null;
            return Task.FromResult(workspace);
        }

        public Task SaveAsync(
            Workspace workspace,
            CancellationToken cancellationToken = default)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException("Simulated persistence failure.");
            }

            StoredWorkspace = Clone(workspace);
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

        public Task ReplaceActiveNotesAsync(WorkspaceId workspaceId, IReadOnlyCollection<Note> importedNotes, CancellationToken cancellationToken = default) => Task.CompletedTask;

        private static Workspace Clone(Workspace workspace)
        {
            return new Workspace(
                workspace.Id,
                workspace.Name,
                workspace.CreatedAt,
                workspace.UpdatedAt,
                workspace.Notes.Select(note => new Note(
                    note.Id,
                    note.BoardKey,
                    note.Priority,
                    note.Title,
                    note.Content,
                    note.Position,
                    note.Size,
                    note.Color,
                    note.ZIndex,
                    note.IsCompleted,
                    note.CreatedAt,
                    note.UpdatedAt,
                    plannedDate: note.PlannedDate,
                    completedAt: note.CompletedAt)));
        }
    }
}
