using ConvenientNote.Application.Abstractions;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Infrastructure.Persistence;
using ConvenientNote.Services;
using ConvenientNote.Views;
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
            containerRegistry.RegisterSingleton<OpenMeteoWeatherService>();
            containerRegistry.RegisterSingleton<NoteMediaService>();
            containerRegistry.RegisterSingleton<NotesBackupService>();
            containerRegistry.RegisterSingleton<NotesBackupPackageStager>();
            containerRegistry.RegisterSingleton<WorkspaceTransferRequestGate>();
            containerRegistry.RegisterSingleton<RichTextDocumentService>();
            containerRegistry.RegisterForNavigation<DayTodoView>();
            containerRegistry.RegisterForNavigation<NotesView>();
            containerRegistry.RegisterForNavigation<ScheduleView>();
            containerRegistry.RegisterForNavigation<InboxView>();
            containerRegistry.RegisterForNavigation<ReviewView>();
            containerRegistry.RegisterForNavigation<CompletedTodoView>();
            containerRegistry.RegisterForNavigation<TrashView>();
        }
    }
}
