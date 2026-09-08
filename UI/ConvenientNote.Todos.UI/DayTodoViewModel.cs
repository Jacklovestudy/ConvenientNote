using ConvenientNote.Todos.Application;
using ConvenientNote.Services;

namespace ConvenientNote.ViewModels
{
    public sealed class DayTodoViewModel : TodoBoardViewModel
    {
        public DayTodoViewModel(TodoApplicationService todoApplicationService, ITodoWeatherService weatherService) : base(todoApplicationService, weatherService, "day-todo", TodoBoardFilter.Active, "今日待办", "今天要处理的待办", true, filterByDate: true)
        {
        }
    }
}
