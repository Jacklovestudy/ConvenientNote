using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConvenientNote.UI.Common;
using ConvenientNote.ViewModels;

namespace ConvenientNote.Desktop.Smoke;

internal static class Program
{
    private static string _directory = "";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Pass an isolated smoke data directory.");
        _directory = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(_directory);
        Environment.SetEnvironmentVariable("CONVENIENTNOTE_DATA_DIRECTORY", _directory);
        var app = new App();
        app.DispatcherUnhandledException += (_, e) => Fail(e.Exception);
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is not MainWindow window) return;
                window.Left = -20000;
                window.Top = -20000;
                window.ShowInTaskbar = false;
                window.Dispatcher.BeginInvoke(new Action(async () =>
                {
                    try { await VerifyAsync(window); Environment.Exit(0); }
                    catch (Exception error) { Fail(error); }
                }), DispatcherPriority.ApplicationIdle);
            }));
        app.InitializeComponent();
        return app.Run();
    }

    private static async Task VerifyAsync(MainWindow window)
    {
        var model = window.DataContext as MainWindowViewModel ?? throw new InvalidOperationException("Shell ViewModel not resolved.");
        if (model.ActiveNavigationItem?.Section != NavigationSection.Notes)
            throw new InvalidOperationException("Startup did not select Notes.");
        var region = (ContentControl)window.FindName("MainRegionContent");
        foreach (var item in model.NavigationItems)
        {
            if (region.Content is IPageLifecycle lifecycle && !await lifecycle.FlushAsync())
                throw new InvalidOperationException("Page failed to flush.");
            model.ActiveNavigationItem = item;
            for (var attempt = 0; attempt < 60 && (region.Content is not FrameworkElement page || page.GetType().Name != item.ViewName); attempt++)
                await Task.Delay(50);
            var current = region.Content as FrameworkElement;
            if (current?.GetType().Name != item.ViewName || current.DataContext is null)
                throw new InvalidOperationException($"Navigation failed: {item.ViewName}, current={current?.GetType().Name}, model={current?.DataContext?.GetType().Name}");
            if (current.DataContext is NotesViewModel notes)
            {
                const string relativeImage = "smoke/image.png";
                var imagePath = Path.Combine(_directory, "Notes", "Media", relativeImage);
                Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
                var pixel = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 50, 100, 150, 255 }, 4);
                var imageEncoder = new PngBitmapEncoder();
                imageEncoder.Frames.Add(BitmapFrame.Create(pixel));
                using (var output = File.Create(imagePath)) imageEncoder.Save(output);
                var document = new FlowDocument(new Paragraph(new InlineUIContainer(new Image { Tag = relativeImage, Width = 1 })));
                var saved = notes.DocumentService.Save(document);
                var loaded = notes.DocumentService.Load(saved.Json, "");
                var loadedImage = (Image)((InlineUIContainer)((Paragraph)loaded.Blocks.FirstBlock).Inlines.FirstInline).Child;
                if (loadedImage.Source is not BitmapSource { PixelWidth: 1 })
                    throw new InvalidOperationException("Notes editor did not resolve the module media directory.");
                Console.WriteLine("PASS module image resolution");
            }
            if (current.DataContext is ScheduleViewModel calendar)
            {
                await calendar.RefreshAsync();
                calendar.NewEventTitle = "独立日程烟雾测试";
                await calendar.AddEventAsync();
                if (!calendar.SelectedTasks.Any(t => t.IsEvent && t.Title == "独立日程烟雾测试"))
                    throw new InvalidOperationException("Native calendar event was not displayed.");
                if (!string.IsNullOrEmpty(calendar.ErrorMessage)) throw new InvalidOperationException(calendar.ErrorMessage);
                calendar.DesktopModeCommand.Execute();
                for (var attempt = 0; attempt < 60 && !window.IsCompactCalendar; attempt++) await Task.Delay(50);
                if (!window.IsCompactCalendar) throw new InvalidOperationException("Compact module provider did not activate.");
                window.ExitCompactCalendar();
                Console.WriteLine("PASS compact calendar provider");
            }
            window.UpdateLayout();
            if (item.Section is NavigationSection.Notes or NavigationSection.Schedule or NavigationSection.ColorPicker)
            {
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(_directory, item.Section + ".png"));
                encoder.Save(output);
            }
            Console.WriteLine($"PASS {item.ViewName}: {current.DataContext.GetType().Name}");
        }
        model.ActiveNavigationItem = model.NavigationItems.Single(i => i.Section == NavigationSection.Notes);
        Console.WriteLine("PASS startup, all navigation routes, native calendar persistence, module resources");
    }

    private static void Fail(Exception error)
    {
        Console.Error.WriteLine(error);
        File.WriteAllText(Path.Combine(_directory, "failure.txt"), error.ToString());
        Environment.Exit(1);
    }
}
