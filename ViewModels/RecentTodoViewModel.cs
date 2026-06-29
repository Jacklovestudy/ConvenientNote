using ConvenientNote.Application.Workspaces;

namespace ConvenientNote.ViewModels
{
    public sealed class RecentTodoViewModel : TodoBoardViewModel
    {
        public RecentTodoViewModel(WorkspaceApplicationService workspaceApplicationService)
            : base(
                workspaceApplicationService,
                TodoBoardFilter.All,
                "最近待办",
                "最近创建和更新的便签",
                true)
        {
        }
    }
}
