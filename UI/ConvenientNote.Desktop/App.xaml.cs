using System.IO;
using System.Windows;
using ConvenientNote.LegacyMigration;
using ConvenientNote.UI.Common;
using ConvenientNote.DesktopPet.UI;
using ConvenientNote.ViewModels;
using Prism.DryIoc;
using Prism.Ioc;

namespace ConvenientNote;

public partial class App : PrismApplication
{
    private static string DataDirectory => Environment.GetEnvironmentVariable("CONVENIENTNOTE_DATA_DIRECTORY")
        is { Length: > 0 } customDirectory ? Path.GetFullPath(customDirectory)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConvenientNote");

    protected override async void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            await new LegacyDataMigration(DataDirectory).InitializeAsync();
            base.OnStartup(e);
        }
        catch (Exception exception)
        {
            MaterialDialogWindow.Inform(null, "启动失败",
                "软件初始化失败，原始数据已保留。请关闭其他实例后重试。\n\n" + exception.Message);
            Shutdown(1);
        }
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        if (MainWindow is MainWindow window)
        {
            window.InitializeCompactMode(Container.Resolve<ICompactModeProvider>());
            var pet = Container.Resolve<PetController>();
            pet.OpenSettings += () =>
            {
                window.ExitCompactCalendar();
                window.Show();
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                window.Activate();
                if (window.DataContext is MainWindowViewModel model)
                    model.ActiveNavigationItem = model.NavigationItems.Single(i => i.Section == NavigationSection.DesktopPet);
            };
            pet.Restore();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (Container is not null && Container.IsRegistered<PetController>()) Container.Resolve<PetController>().Shutdown();
        base.OnExit(e);
    }

    protected override Window CreateShell() => Container.Resolve<MainWindow>();

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
        => DesktopModules.Register(containerRegistry, DataDirectory);
}
