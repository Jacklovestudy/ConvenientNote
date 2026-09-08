using ConvenientNote.Todos.Application;
using ConvenientNote.Services;

namespace ConvenientNote.ViewModels
{
    public sealed class CompletedTodoViewModel : TodoBoardViewModel
    {
        public CompletedTodoViewModel(TodoApplicationService todoApplicationService, ITodoWeatherService weatherService) : base(todoApplicationService, weatherService, "day-todo", TodoBoardFilter.Completed, "已达成", "已经完成的事项", false)
        {
        }
    }
}
