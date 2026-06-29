using ConvenientNote.Application.Workspaces;
using ConvenientNote.Services;

namespace ConvenientNote.ViewModels
{
    public sealed class DayTodoViewModel : TodoBoardViewModel
    {
        public DayTodoViewModel(
            WorkspaceApplicationService workspaceApplicationService,
            OpenMeteoWeatherService weatherService)
            : base(
                workspaceApplicationService,
                weatherService,
                TodoBoardFilter.Active,
                "Day Todo",
                "今天要处理的待办",
                true)
        {
        }
    }
}
