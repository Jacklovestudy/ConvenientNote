using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using ConvenientNote.Services;
using ConvenientNote.Views;
using MaterialDesignThemes.Wpf;
using Xunit;

namespace ConvenientNote.Tests.Views;

public sealed class DrawerNavigationPerformanceTests
{
    [Fact]
    public void EditingNavigationAnimatesWithoutChangingLayoutAndPreservesDocument() => Sta(() =>
    {
        // Exercise the same drawer type and installed theme used by MainWindow.
        var drawer = (DrawerHost)System.Windows.Markup.XamlReader.Parse("""
            <v:NavigationDrawerHost xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:v="clr-namespace:ConvenientNote.Views;assembly=ConvenientNote"
                xmlns:m="http://materialdesigninxaml.net/winfx/xaml/themes" OpenMode="Modal">
                <m:DrawerHost.Resources>
                    <ResourceDictionary><ResourceDictionary.MergedDictionaries>
                        <m:BundledTheme BaseTheme="Light" PrimaryColor="Indigo" SecondaryColor="Teal" />
                        <ResourceDictionary Source="pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml" />
                    </ResourceDictionary.MergedDictionaries></ResourceDictionary>
                </m:DrawerHost.Resources>
                <m:DrawerHost.LeftDrawerContent><Border Width="296" Background="White"><TextBlock Text="Navigation" /></Border></m:DrawerHost.LeftDrawerContent>
            </v:NavigationDrawerHost>
            """);
        var control = new RichNoteEditorControl();
        var editor = (RichTextBox)control.FindName("Editor");
        var document = new FlowDocument();
        for (var i = 0; i < 100; i++)
        {
            var heading = new Paragraph(new Run($"Chapter {i}"));
            DocumentOutline.SetHeadingLevel(heading, 1);
            document.Blocks.Add(heading);
            document.Blocks.Add(new Paragraph(new Run(new string('x', 300))));
        }
        editor.Document = document;
        var page = new ContentControl { Content = control, DataContext = new EditorState(true) };
        var region = new ContentControl { Content = page };
        drawer.Content = region;
        var window = new Window { Content = drawer, Width = 1400, Height = 820, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            Pump(400);
            var panel = (FrameworkElement)drawer.Template.FindName("PART_LeftDrawer", drawer);
            var cover = (FrameworkElement)drawer.Template.FindName("PART_ContentCover", drawer);
            var text = new TextRange(document.ContentStart, document.ContentEnd).Text;
            editor.CaretPosition = document.Blocks.LastBlock.ContentEnd;
            editor.Selection.Text = " new edit";
            Pump(300);
            var caret = editor.CaretPosition;
            var undo = editor.CanUndo;
            var changes = 0;
            editor.TextChanged += (_, _) => changes++;
            Assert.Null(region.CacheMode);
            for (var i = 0; i < 3; i++)
            {
                drawer.IsLeftDrawerOpen = true;
                Pump(40);
                Assert.True(panel.RenderTransform.HasAnimatedProperties, $"Transform={panel.RenderTransform}, margin={panel.Margin}, x={panel.TransformToAncestor(drawer).Transform(new Point()).X}, disabled={TransitionAssist.GetDisableTransitions(drawer)}, cache={region.CacheMode}");
                Assert.IsType<System.Windows.Media.BitmapCache>(region.CacheMode);
                Assert.InRange(panel.TransformToAncestor(drawer).Transform(new Point()).X, -295.9, -0.1);
                Assert.Equal(new Thickness(0), panel.Margin);
                for (var frame = 0; frame < 12; frame++)
                {
                    Pump(25);
                    Assert.Equal(new Thickness(0), panel.Margin);
                }
                Assert.InRange(Math.Abs(panel.TransformToAncestor(drawer).Transform(new Point()).X), 0, 1);
                WaitFor(() => region.CacheMode is null);
                Assert.True(cover.IsHitTestVisible);
                Assert.True(cover.Opacity > 0);
                DrawerHost.CloseDrawerCommand.Execute(Dock.Left, drawer);
                Pump(40);
                Assert.True(panel.RenderTransform.HasAnimatedProperties);
                Pump(350);
                Assert.InRange(panel.TransformToAncestor(drawer).Transform(new Point()).X, -297, -295);
                Assert.False(cover.IsVisible && cover.IsHitTestVisible, $"Cover opacity={cover.Opacity}, visibility={cover.Visibility}, disabled={TransitionAssist.GetDisableTransitions(drawer)}");
                WaitFor(() => region.CacheMode is null);
            }

            var existingCache = new System.Windows.Media.BitmapCache();
            region.CacheMode = existingCache;
            drawer.IsLeftDrawerOpen = true;
            Pump(50);
            Assert.IsType<System.Windows.Media.BitmapCache>(region.CacheMode);
            Assert.NotSame(existingCache, region.CacheMode);
            var beforeReverse = panel.TransformToAncestor(drawer).Transform(new Point()).X;
            drawer.IsLeftDrawerOpen = false;
            Assert.InRange(Math.Abs(panel.TransformToAncestor(drawer).Transform(new Point()).X - beforeReverse), 0, 1);
            Pump(50);
            // A cancelled opening clock must not release the cache while closing is active.
            Assert.IsType<System.Windows.Media.BitmapCache>(region.CacheMode);
            Assert.NotSame(existingCache, region.CacheMode);
            drawer.IsLeftDrawerOpen = true;
            Pump(350);
            Assert.InRange(Math.Abs(panel.TransformToAncestor(drawer).Transform(new Point()).X), 0, 1);
            WaitFor(() => ReferenceEquals(existingCache, region.CacheMode));
            Assert.Same(existingCache, region.CacheMode);
            Assert.IsType<Border>(drawer.LeftDrawerContent).Width = 330;
            Pump(40);
            drawer.IsLeftDrawerOpen = false;
            Pump(350);
            Assert.InRange(panel.TransformToAncestor(drawer).Transform(new Point()).X, -331, -329);
            WaitFor(() => ReferenceEquals(existingCache, region.CacheMode));
            Assert.Same(existingCache, region.CacheMode);
            Assert.False(cover.IsVisible && cover.IsHitTestVisible);
            Assert.Equal(0, changes);
            Assert.Same(document, editor.Document);
            Assert.Equal(0, caret.CompareTo(editor.CaretPosition));
            Assert.Equal(undo, editor.CanUndo);
            editor.Undo();
            Assert.Equal(text, new TextRange(document.ContentStart, document.ContentEnd).Text);
            Assert.False(TransitionAssist.GetDisableTransitions(drawer));
            page.DataContext = new EditorState(false);
            Pump(40);
            Assert.False(TransitionAssist.GetDisableTransitions(drawer));
            region.Content = new TextBlock { Text = "Other page" };
            Pump(40);
            Assert.False(TransitionAssist.GetDisableTransitions(drawer));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void DisabledTransitionsRetemplatingAndUnloadingRestoreTheOriginalCache() => Sta(() =>
    {
        var drawer = (NavigationDrawerHost)System.Windows.Markup.XamlReader.Parse("""
            <v:NavigationDrawerHost xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:v="clr-namespace:ConvenientNote.Views;assembly=ConvenientNote"
                xmlns:m="http://materialdesigninxaml.net/winfx/xaml/themes" OpenMode="Modal">
                <m:DrawerHost.Resources>
                    <ResourceDictionary><ResourceDictionary.MergedDictionaries>
                        <m:BundledTheme BaseTheme="Light" PrimaryColor="Indigo" SecondaryColor="Teal" />
                        <ResourceDictionary Source="pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml" />
                    </ResourceDictionary.MergedDictionaries></ResourceDictionary>
                </m:DrawerHost.Resources>
                <m:DrawerHost.LeftDrawerContent><Border Width="296" Background="White" /></m:DrawerHost.LeftDrawerContent>
            </v:NavigationDrawerHost>
            """);
        var originalCache = new System.Windows.Media.BitmapCache();
        var content = new Border { CacheMode = originalCache };
        drawer.Content = content;
        var window = new Window { Content = drawer, Width = 960, Height = 600, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            Pump(300);
            TransitionAssist.SetDisableTransitions(drawer, true);
            drawer.IsLeftDrawerOpen = true;
            Pump(40);
            Assert.Same(originalCache, content.CacheMode);
            Pump(300);

            TransitionAssist.SetDisableTransitions(drawer, false);
            drawer.IsLeftDrawerOpen = false;
            Pump(40);
            Assert.NotSame(originalCache, content.CacheMode);
            var oldRoot = (FrameworkElement)System.Windows.Media.VisualTreeHelper.GetChild(drawer, 0);
            var oldPanel = (FrameworkElement)drawer.Template.FindName("PART_LeftDrawer", drawer);
            var template = drawer.Template;
            drawer.Template = null;
            drawer.ApplyTemplate();
            Assert.Same(originalCache, content.CacheMode);
            Pump(350); // A long-lived null template must release references and controllable clocks.
            Assert.Null(System.Windows.VisualStateManager.GetCustomVisualStateManager(oldRoot));
            Assert.False(oldPanel.RenderTransform.HasAnimatedProperties);
            foreach (var name in new[] { "_leftGroup", "_panel", "_slide", "_stateRoot", "_stateManager" })
            {
                Assert.Null(typeof(NavigationDrawerHost).GetField(name,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(drawer));
            }
            drawer.Template = template;
            drawer.ApplyTemplate();
            Pump(300);
            Assert.Same(originalCache, content.CacheMode);

            drawer.IsLeftDrawerOpen = true;
            Pump(40);
            Assert.NotSame(originalCache, content.CacheMode);
            window.Content = null;
            Pump(40);
            Assert.Same(originalCache, content.CacheMode);
            window.Content = drawer;
            Pump(300);
            drawer.IsLeftDrawerOpen = false;
            Pump(350);
            WaitFor(() => ReferenceEquals(originalCache, content.CacheMode));
        }
        finally { window.Close(); }
    });

    private sealed record EditorState(bool IsEditorOpen);

    private static void WaitFor(Func<bool> condition)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(2)) Pump(20);
        Assert.True(condition(), "Animation completion did not restore the background cache.");
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
