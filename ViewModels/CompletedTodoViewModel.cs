using ConvenientNote.Application.Workspaces;

namespace ConvenientNote.ViewModels
{
    public sealed class CompletedTodoViewModel : TodoBoardViewModel
    {
        public CompletedTodoViewModel(WorkspaceApplicationService workspaceApplicationService)
            : base(
                workspaceApplicationService,
                TodoBoardFilter.Completed,
                "已达成",
                "已经完成的事项",
                false)
        {
        }
    }
}
