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
    private static ConvenientNote.DesktopPet.UI.PelicanWindow? _petWindow;

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
                if (sender is ConvenientNote.DesktopPet.UI.PelicanWindow petWindow)
                {
                    petWindow.Opacity = 0;
                    _petWindow = petWindow;
                    return;
                }
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
            if (current.DataContext is ConvenientNote.DesktopPet.UI.PetController pet)
            {
                pet.Show();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                if (!pet.IsVisible || _petWindow is null) throw new InvalidOperationException("Pet could not be shown: " + pet.Status);
                var firstWindow = _petWindow;
                var phase = firstWindow.Artwork.Phase;
                await Task.Delay(150);
                if (phase == firstWindow.Artwork.Phase) throw new InvalidOperationException("Pet animation did not advance.");
                var speedSlider = (Slider)current.FindName("SpeedSlider");
                speedSlider.Value = 40;
                await Task.Delay(350);
                if (pet.RidingSpeed != 40 || firstWindow.Motion.RidingSpeed != 40) throw new InvalidOperationException("Speed slider did not update the live pet.");
                firstWindow.Motion.Boost();
                if (firstWindow.Motion.Speed != 80) throw new InvalidOperationException("Boost is not twice the configured speed.");
                pet.SetRidingSpeed(55);
                if (firstWindow.Motion.Speed != 110) throw new InvalidOperationException("Boost did not track the changed speed.");
                pet.SetScale(.8);
                if (Math.Abs(firstWindow.Width - 208) > .01) throw new InvalidOperationException("Pet size was not applied.");
                pet.Shutdown();
                var stoppedPhase = firstWindow.Artwork.Phase;
                await Task.Delay(100);
                if (stoppedPhase != firstWindow.Artwork.Phase) throw new InvalidOperationException("Pet timer survived shutdown.");
                pet.Restore();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                if (!pet.IsVisible || ReferenceEquals(firstWindow, _petWindow)) throw new InvalidOperationException("Pet did not restore after shutdown.");
                if (_petWindow!.Motion.RidingSpeed != 55) throw new InvalidOperationException("Pet did not restore configured speed.");
                pet.Hide();
                if (pet.IsVisible) throw new InvalidOperationException("Pet did not hide.");
                pet.SetScale(1);
                pet.SetRidingSpeed(22);
                Console.WriteLine("PASS speed slider, live boost multiplier and restored speed");
                Console.WriteLine("PASS pet show, animation, resize, shutdown, restore and hide");
            }
            window.UpdateLayout();
            VerifyScrollBars(current);
            if (item.Section is NavigationSection.Notes or NavigationSection.Schedule or NavigationSection.ColorPicker or NavigationSection.DesktopPet)
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
        RenderPelicanActions();
        var editorSamples = new StackPanel();
        editorSamples.Children.Add(new TextBox { Height = 80, AcceptsReturn = true, Text = string.Join("\n", Enumerable.Repeat("滚动条检查", 30)), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        editorSamples.Children.Add(new ListBox { Height = 80, ItemsSource = Enumerable.Range(1, 30) });
        editorSamples.Children.Add(new RichTextBox { Height = 80, Document = new FlowDocument(new Paragraph(new Run(new string('文', 2000)))), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        editorSamples.Children.Add(new ConvenientNote.Views.CodeBlockControl { Height = 160 });
        var editorWindow = new Window { Content = editorSamples, Width = 480, Height = 480, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
        editorWindow.Show();
        try
        {
            await editorWindow.Dispatcher.InvokeAsync(() => editorWindow.UpdateLayout(), DispatcherPriority.ApplicationIdle);
            VerifyScrollBars(editorSamples);
            Console.WriteLine("PASS shared scrollbars in TextBox, ListBox, RichTextBox and code editor");
        }
        finally { editorWindow.Close(); }
        EventManager.RegisterClassHandler(typeof(MaterialDialogWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is not MaterialDialogWindow dialog) return;
                dialog.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        dialog.UpdateLayout();
                        var preview = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        preview.Render(dialog);
                        var png = new PngBitmapEncoder();
                        png.Frames.Add(BitmapFrame.Create(preview));
                        using (var output = File.Create(Path.Combine(_directory, "Confirmation.png"))) png.Save(output);
                        dialog.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                            PresentationSource.FromVisual(dialog)!, 0, System.Windows.Input.Key.Escape)
                            { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
                    }
                    catch (Exception error) { Fail(error); }
                }), DispatcherPriority.ApplicationIdle);
            }));
        if (MaterialDialogWindow.Confirm(window, "退出 Convenient Note？", "退出前会保存当前编辑的内容。", "退出软件"))
            throw new InvalidOperationException("Escape must cancel the confirmation.");
        Console.WriteLine("PASS Material Design confirmation resources and Escape cancellation");
        Console.WriteLine("PASS startup, all navigation routes, native calendar persistence, module resources");
    }

    private static void Fail(Exception error)
    {
        Console.Error.WriteLine(error);
        File.WriteAllText(Path.Combine(_directory, "failure.txt"), error.ToString());
        Environment.Exit(1);
    }

    private static void VerifyScrollBars(DependencyObject root)
    {
        if (root is System.Windows.Controls.Primitives.ScrollBar bar)
        {
            var expected = (Style)bar.FindResource("UnifiedScrollBar");
            var style = bar.Style;
            while (style is not null && !ReferenceEquals(style, expected)) style = style.BasedOn;
            if (style is null) throw new InvalidOperationException($"ScrollBar in {bar.TemplatedParent?.GetType().Name} bypasses the shared style.");
        }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            VerifyScrollBars(VisualTreeHelper.GetChild(root, index));
    }

    private static void RenderPelicanActions(bool interaction = false, bool collision = false)
    {
        var sheet = new DrawingVisual();
        using (var drawing = sheet.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(241, 246, 238)), null, new Rect(0, 0, 1080, 700));
            var actions = Enum.GetValues<ConvenientNote.DesktopPet.Domain.PetAction>().Take(6).ToArray();
            string[] labels = interaction ? ["视线跟随", "悬停歪头", "摸头眯眼", "向左互动", "缓缓醒来", "打盹等待"] : ["慢骑", "加速", "刹车", "歪头", "拎起", "打盹"];
            if (collision) labels = ["碰撞", "滑下车", "坐着发懵", "爬回车座", "重新上车", "往回骑"];
            for (var i = 0; i < actions.Length; i++)
            {
                var artwork = new ConvenientNote.DesktopPet.UI.PelicanVisual();
                if (interaction)
                {
                    artwork.IsAttentive = true;
                    artwork.AttentionTilt = i is 1 or 2 or 3 ? 1 : 0;
                    artwork.IsPetting = i == 2;
                    artwork.Pointer = new Point(190, 28);
                    artwork.RestAmount = i == 4 ? .5 : i == 5 ? 1 : 0;
                }
                artwork.Measure(new Size(360, 310)); artwork.Arrange(new Rect(0, 0, 360, 310));
                artwork.Update(interaction ? (i == 5 ? ConvenientNote.DesktopPet.Domain.PetAction.Sleep : ConvenientNote.DesktopPet.Domain.PetAction.Ride) : actions[i], .7, .4, interaction && i == 3 ? -1 : 1); artwork.UpdateLayout();
                if (collision)
                {
                    double[] ages = [.15, .55, 1.2, 2.2, 2.85, 0];
                    artwork.Update(i == 5 ? ConvenientNote.DesktopPet.Domain.PetAction.Ride : ConvenientNote.DesktopPet.Domain.PetAction.Crash, .7, ages[i], i == 5 ? -1 : 1);
                    artwork.UpdateLayout();
                }
                var frame = new RenderTargetBitmap(360, 310, 96, 96, PixelFormats.Pbgra32); frame.Render(artwork);
                var x = i % 3 * 360; var y = i / 3 * 350;
                drawing.DrawImage(frame, new Rect(x, y, 360, 310));
                drawing.DrawText(new FormattedText(labels[i], System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Microsoft YaHei UI"), 18, Brushes.DarkSlateGray, 1), new Point(x + 155, y + 315));
            }
        }
        var bitmap = new RenderTargetBitmap(1080, 700, 96, 96, PixelFormats.Pbgra32); bitmap.Render(sheet);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(_directory, collision ? "PelicanCollision.png" : interaction ? "PelicanInteraction.png" : "PelicanActions.png")); png.Save(file);
        if (!interaction && !collision) { RenderPelicanActions(true); RenderPelicanActions(false, true); }
    }
}
