using System.IO;
using System.Reflection;
using System.Windows;
using ConvenientNote.Platform.Contracts;
using ConvenientNote.Todos.Application;
using ConvenientNote.Todos.Domain;

using ConvenientNote.Services;
using ConvenientNote.ViewModels;
using Xunit;

namespace ConvenientNote.Tests.ViewModels;

public sealed class TodoBoardViewModelTests
{
    [Fact]
    public async Task DateSelectionFiltersScheduledTasksWhileInboxKeepsUnscheduledTasks()
    {
        var repository = new InMemoryTodoRepository();
        var service = new TodoApplicationService(repository, new WorkspaceContext());
        var workspace = await service.GetCurrentAsync();
        var today = await service.CreateTodoAsync(workspace.Id, 32, 32, "今天", DateTime.Today);
        var tomorrow = await service.CreateTodoAsync(workspace.Id, 32, 32, "明天", DateTime.Today.AddDays(1));
        await service.CreateTodoAsync(workspace.Id, 0, 0, "未安排");
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
        Assert.Empty(repository.ActiveItems);
    }

    [Fact]
    public async Task CommitTodoTitleAsync_AfterSuccessfulDeletion_DoesNotRestoreOrThrowForStaleTodo()
    {
        var (viewModel, repository, todo) = await CreateLoadedViewModelAsync();
        todo.Title = "Stale title";

        await viewModel.DeleteTodoAsync(todo);
        var exception = await Record.ExceptionAsync(() => viewModel.CommitTodoTitleAsync(todo));

        Assert.Null(exception);
        Assert.Empty(repository.ActiveItems);
    }

    [Fact]
    public async Task CommitTodoContentAsync_AfterSuccessfulDeletion_DoesNotRestoreOrThrowForStaleTodo()
    {
        var (viewModel, repository, todo) = await CreateLoadedViewModelAsync();
        todo.Content = "Stale content";

        await viewModel.DeleteTodoAsync(todo);
        var exception = await Record.ExceptionAsync(() => viewModel.CommitTodoContentAsync(todo));

        Assert.Null(exception);
        Assert.Empty(repository.ActiveItems);
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
        var persistedTodo = Assert.Single(repository.ActiveItems);
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
        Assert.Empty(repository.ActiveItems);
    }

    private static async Task<(DayTodoViewModel ViewModel, InMemoryTodoRepository Repository, CanvasTodoViewModel Todo)>
        CreateLoadedViewModelAsync()
    {
        var repository = new InMemoryTodoRepository();
        var scheduled = TodoItem.Create("Delete me",32,32);
        scheduled.UpdateContent("Original content");
        scheduled.Reschedule(DateTime.Today);
        await repository.SaveAsync(WorkspaceContext.WorkspaceId,[scheduled]);
        var workspaceApplicationService = new TodoApplicationService(repository,new WorkspaceContext());
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

    private sealed class WorkspaceContext : IWorkspaceContext
    {
        public static readonly Guid WorkspaceId=Guid.NewGuid();
        public Task<WorkspaceInfo> GetCurrentAsync(CancellationToken cancellationToken=default)=>Task.FromResult(new WorkspaceInfo(WorkspaceId,"test"));
    }
    private sealed class InMemoryTodoRepository : ITodoRepository
    {
        private readonly Dictionary<TodoId,TodoItem> _items=new();
        public IEnumerable<TodoItem> ActiveItems=>_items.Values.Where(t=>!t.IsDeleted);
        public bool FailNextSave {get;set;}
        public int GetAsyncCallCount {get;private set;}
        public Task<IReadOnlyList<TodoItem>> ListAsync(Guid workspaceId,CancellationToken cancellationToken=default)
        {
            GetAsyncCallCount++;
            return Task.FromResult<IReadOnlyList<TodoItem>>(_items.Values.Select(Clone).ToArray());
        }
        public Task SaveAsync(Guid workspaceId,IReadOnlyList<TodoItem> items,CancellationToken cancellationToken=default)
        {
            if(FailNextSave){FailNextSave=false;throw new IOException("Simulated persistence failure.");}
            foreach(var item in items)_items[item.Id]=Clone(item);
            return Task.CompletedTask;
        }
        private static TodoItem Clone(TodoItem item)=>TodoItem.Restore(item.Id,item.Title,item.Content,item.Priority,item.X,item.Y,item.Width,item.Height,item.Color,item.ZIndex,item.IsCompleted,item.IsDeleted,item.CreatedAt,item.UpdatedAt,item.PlannedDate,item.CompletedAt);
    }
}
