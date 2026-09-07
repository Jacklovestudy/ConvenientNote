using ConvenientNote.Application.Workspaces;
using ConvenientNote.Services;

namespace ConvenientNote.ViewModels
{
    public sealed class CompletedTodoViewModel : TodoBoardViewModel
    {
        public CompletedTodoViewModel(
            WorkspaceApplicationService workspaceApplicationService,
            OpenMeteoWeatherService weatherService)
            : base(
                workspaceApplicationService,
                weatherService,
                TodoBoardKeys.DayTodo,
                TodoBoardFilter.Completed,
                "已达成",
                "已经完成的事项",
                false)
        {
        }
    }
}
