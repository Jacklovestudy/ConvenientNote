using ConvenientNote.Todos.Application;
using ConvenientNote.Services;

namespace ConvenientNote.ViewModels
{
    public sealed class InboxViewModel : TodoBoardViewModel
    {
        public InboxViewModel(TodoApplicationService todoApplicationService, ITodoWeatherService weatherService) : base(todoApplicationService, weatherService, "day-todo", TodoBoardFilter.Active, "待办箱", "所有未完成事项", true)
        {
        }
    }
}
