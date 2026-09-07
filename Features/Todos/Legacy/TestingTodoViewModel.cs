using ConvenientNote.Application.Workspaces;
using ConvenientNote.Services;

namespace ConvenientNote.ViewModels
{
    public sealed class TestingTodoViewModel : TodoBoardViewModel
    {
        public TestingTodoViewModel(
            WorkspaceApplicationService workspaceApplicationService,
            OpenMeteoWeatherService weatherService)
            : base(
                workspaceApplicationService,
                weatherService,
                TodoBoardKeys.Testing,
                TodoBoardFilter.Active,
                "待测试",
                "验证队列",
                true)
        {
        }

        protected override string GetEmptyStateTitle()
        {
            return "还没有待测试事项";
        }

        protected override string GetEmptyStateDescription()
        {
            return "在上方输入内容并创建，新增事项只会出现在待测试画布。";
        }
    }
}
