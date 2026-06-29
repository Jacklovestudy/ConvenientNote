using ConvenientNote.Application.Workspaces;
using ConvenientNote.Services;

namespace ConvenientNote.ViewModels
{
    public sealed class InboxViewModel : TodoBoardViewModel
    {
        public InboxViewModel(
            WorkspaceApplicationService workspaceApplicationService,
            OpenMeteoWeatherService weatherService)
            : base(
                workspaceApplicationService,
                weatherService,
                TodoBoardFilter.Active,
                "待办箱",
                "所有未完成事项",
                true)
        {
        }
    }
}
