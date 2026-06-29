using ConvenientNote.Application.Workspaces;

namespace ConvenientNote.ViewModels
{
    public sealed class DayTodoViewModel : TodoBoardViewModel
    {
        public DayTodoViewModel(WorkspaceApplicationService workspaceApplicationService)
            : base(
                workspaceApplicationService,
                TodoBoardFilter.Active,
                "Day Todo",
                "今天要处理的待办",
                true)
        {
        }
    }
}
