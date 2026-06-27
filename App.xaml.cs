using ConvenientNote.Application.Abstractions;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Infrastructure.Persistence;
using Prism.DryIoc;
using Prism.Ioc;
using System.Windows;

namespace ConvenientNote
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : PrismApplication
    {
        protected override Window CreateShell()
        {
            return Container.Resolve<MainWindow>();
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterSingleton<IWorkspaceRepository, SqliteWorkspaceRepository>();
            containerRegistry.RegisterSingleton<WorkspaceApplicationService>();
        }
    }
}
