using ConvenientNote.Application.Workspaces;

namespace ConvenientNote.ViewModels
{
    public sealed class InboxViewModel : TodoBoardViewModel
    {
        public InboxViewModel(WorkspaceApplicationService workspaceApplicationService)
            : base(
                workspaceApplicationService,
                TodoBoardFilter.Active,
                "待办箱",
                "所有未完成事项",
                true)
        {
        }
    }
}
