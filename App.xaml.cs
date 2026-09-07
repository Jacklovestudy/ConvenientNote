using ConvenientNote.Application.Abstractions;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Infrastructure.Persistence;
using ConvenientNote.Services;
using ConvenientNote.Views;
using Prism.DryIoc;
using Prism.Ioc;
using System.Windows;
using System.IO;
using ConvenientNote.ViewModels;

namespace ConvenientNote
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : PrismApplication
    {

        private static string DataDirectory => Environment.GetEnvironmentVariable("CONVENIENTNOTE_DATA_DIRECTORY")
            is { Length: > 0 } customDirectory ? Path.GetFullPath(customDirectory)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConvenientNote");

        protected override void OnInitialized()
        {
            base.OnInitialized();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            if (MainWindow is not MainWindow window) return;
            var calendar = Container.Resolve<ScheduleViewModel>();
            window.InitializeCompactCalendar(calendar);
        }

        protected override Window CreateShell()
        {
            return Container.Resolve<MainWindow>();
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterInstance<IWorkspaceRepository>(new SqliteWorkspaceRepository(Path.Combine(DataDirectory, "ConvenientNote.db")));
            containerRegistry.RegisterSingleton<WorkspaceApplicationService>();
            containerRegistry.RegisterSingleton<OpenMeteoWeatherService>();
            containerRegistry.RegisterInstance(new NoteMediaService(Path.Combine(DataDirectory, "Media")));
            containerRegistry.RegisterSingleton<ScheduleViewModel>();
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
