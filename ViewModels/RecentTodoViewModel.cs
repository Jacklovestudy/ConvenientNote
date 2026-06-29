using ConvenientNote.Application.Workspaces;
using ConvenientNote.Services;

namespace ConvenientNote.ViewModels
{
    public sealed class RecentTodoViewModel : TodoBoardViewModel
    {
        public RecentTodoViewModel(
            WorkspaceApplicationService workspaceApplicationService,
            OpenMeteoWeatherService weatherService)
            : base(
                workspaceApplicationService,
                weatherService,
                TodoBoardFilter.All,
                "最近待办",
                "最近创建和更新的便签",
                true)
        {
        }
    }
}
