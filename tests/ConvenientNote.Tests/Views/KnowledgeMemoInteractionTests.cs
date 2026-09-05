using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ConvenientNote.Application.Abstractions;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;
using ConvenientNote.Services;
using ConvenientNote.ViewModels;
using ConvenientNote.Views;
using Prism.Mvvm;
using Prism.Navigation.Regions;
using Xunit;

namespace ConvenientNote.Tests.Views;

public sealed class KnowledgeMemoInteractionTests
{
    [Fact]
    public void SidebarSupportsEditingChecksDeleteUndoAndFailureWithoutLosingDraft() => Sta(() =>
    {
        var repository = new Repository();
        var service = new WorkspaceApplicationService(repository);
        var media = new NoteMediaService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var model = new NotesViewModel(service, new RichTextDocumentService(), media);
        model.InitializeAsync().GetAwaiter().GetResult();
        ViewModelLocationProvider.SetDefaultViewModelFactory((_, type) => type == typeof(NotesViewModel) ? model : Activator.CreateInstance(type)!);
        var view = new NotesView(new NotesBackupService(service, media), new NotesBackupPackageStager(), new WorkspaceTransferRequestGate(), new RegionManager()) { DataContext = model };
        view.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse("""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:m="http://materialdesigninxaml.net/winfx/xaml/themes">
              <ResourceDictionary.MergedDictionaries><m:BundledTheme BaseTheme="Light" PrimaryColor="Indigo" SecondaryColor="Teal" />
                <ResourceDictionary Source="pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml" />
              </ResourceDictionary.MergedDictionaries>
            </ResourceDictionary>
            """));
        var window = new Window { Content = view, Width = 1600, Height = 920, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            Drain(view);
            var memo = (KnowledgeMemoControl)view.FindName("KnowledgeMemo");
            Assert.InRange(memo.ActualWidth, 300, 520);
            Assert.True(memo.TransformToAncestor(view).Transform(new Point()).X > 1000);
            Assert.Contains("35/149", ((TextBlock)memo.FindName("ProgressText")).Text);
            var output = Environment.GetEnvironmentVariable("CONVENIENT_NOTE_KNOWLEDGE_PREVIEW");
            if (output is not null)
            {
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(view);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var stream = File.Create(output); encoder.Save(stream);
            }
            var checklist = (ListBox)memo.FindName("Checklist");
            var originalText = model.KnowledgeMemoText;
            var firstHeading = checklist.Items.Cast<KnowledgeRow>().First(r => r.IsHeading);
            var nextHeading = checklist.Items.Cast<KnowledgeRow>().Where(r => r.IsHeading).Skip(1).First();
            var headingButton = Descendants<Button>(checklist).First(b => b.DataContext is KnowledgeRow r && r.LineIndex == firstHeading.LineIndex);
            AssertHeadingFits(headingButton);
            memo.Width = 300;
            Drain(view);
            AssertHeadingFits(headingButton);
            memo.ClearValue(FrameworkElement.WidthProperty);
            Drain(view);
            headingButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(view);
            Assert.True(checklist.Items.Cast<KnowledgeRow>().First().IsCollapsed);
            Assert.DoesNotContain(checklist.Items.Cast<KnowledgeRow>(), r => r.LineIndex > firstHeading.LineIndex && r.LineIndex < nextHeading.LineIndex);
            Assert.Contains(checklist.Items.Cast<KnowledgeRow>(), r => r.LineIndex == nextHeading.LineIndex);
            Assert.Equal(originalText, model.KnowledgeMemoText);
            Assert.Contains("35/149", ((TextBlock)memo.FindName("ProgressText")).Text);
            var otherCheck = Descendants<CheckBox>(checklist).First(c => c.IsVisible && c.DataContext is KnowledgeRow { HasCheck: true });
            otherCheck.IsChecked = otherCheck.IsChecked != true;
            otherCheck.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Drain(view);
            Assert.True(checklist.Items.Cast<KnowledgeRow>().First().IsCollapsed);
            headingButton = Descendants<Button>(checklist).First(b => b.DataContext is KnowledgeRow r && r.LineIndex == firstHeading.LineIndex);
            headingButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(view);
            Assert.Equal(8, checklist.Items.Cast<KnowledgeRow>().Count(r => r.HasCheck && r.LineIndex > firstHeading.LineIndex && r.LineIndex < nextHeading.LineIndex));
            var scroll = Descendants<ScrollViewer>(checklist).First();
            scroll.ScrollToVerticalOffset(900);
            Drain(view);
            var scrolledCheck = Descendants<CheckBox>(checklist).First(c => c.DataContext is KnowledgeRow { LineIndex: > 20 });
            scrolledCheck.IsChecked = scrolledCheck.IsChecked != true;
            scrolledCheck.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Drain(view);
            Assert.True(scroll.VerticalOffset > 800, "Checking a lower item must not jump back to the top.");
            Click(memo, "EditMemoButton");
            var editor = (TextBox)memo.FindName("MemoEditor");
            const string text = "### 自定义　已掌握 0/2\n1. 数组　☐\n2. 内存泄漏　☐\n可以自由添加的说明";
            editor.Text = text;
            Click(memo, "CancelMemoButton");
            Assert.NotEqual(text, model.KnowledgeMemoText);
            Click(memo, "EditMemoButton");
            editor.Text = text;
            Assert.True(memo.SaveChangesAsync().GetAwaiter().GetResult());
            Assert.Equal(text, model.KnowledgeMemoText);
            Drain(view);
            var check = Descendants<CheckBox>(memo).First(c => c.IsVisible && c.DataContext is KnowledgeRow);
            check.IsChecked = true;
            check.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Assert.Contains("已掌握 1/2", model.KnowledgeMemoText);
            var beforeDelete = model.KnowledgeMemoText;
            Click(memo, "DeleteMemoButton");
            Assert.Empty(model.KnowledgeMemoText);
            Click(memo, "UndoDeleteButton");
            Assert.Equal(beforeDelete, model.KnowledgeMemoText);
            Click(memo, "EditMemoButton");
            editor.Text = "失败后应保留的草稿";
            repository.Fail = true;
            Assert.False(memo.SaveChangesAsync().GetAwaiter().GetResult());
            Assert.Equal("失败后应保留的草稿", editor.Text);
            Assert.Contains("保存失败", ((TextBlock)memo.FindName("StatusText")).Text);
            repository.Fail = false;
            Assert.True(view.FlushAsync().GetAwaiter().GetResult());
            Assert.Equal(editor.Text, model.KnowledgeMemoText);
        }
        finally { window.Close(); }
    });

    private static void Click(KnowledgeMemoControl control, string name) => ((Button)control.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void AssertHeadingFits(Button button)
    {
        var label = Descendants<TextBlock>(button).First(t => t.Text == ((KnowledgeRow)button.DataContext).Text);
        var required = label.ActualHeight + button.Padding.Top + button.Padding.Bottom;
        Assert.True(button.ActualHeight >= required - 0.5,
            $"Heading clipped: button={button.ActualHeight}, text={label.ActualHeight}, required={required}, Height={button.Height}");
    }
    private static void Drain(FrameworkElement element) { element.UpdateLayout(); element.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); element.UpdateLayout(); }
    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private sealed class Repository : IWorkspaceRepository
    {
        public Workspace Workspace { get; } = Workspace.Create("默认工作区");
        public bool Fail;
        public Repository()
        {
            var names = new[] { "C# 随记", "内存泄漏", "泛型" };
            for (var i = 0; i < names.Length; i++)
                Workspace.AddNote(TodoBoardKeys.Notes, names[i], "记录代码、学习要点和练习结果。", new NotePosition(32 + i * 304, 32), new NoteSize(280, 180), "#FFF8B8");
        }
        public Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Workspace>>([Workspace]);
        public Task<Workspace?> GetAsync(WorkspaceId id, CancellationToken cancellationToken = default) => Task.FromResult<Workspace?>(Workspace);
        public Task SaveAsync(Workspace workspace, CancellationToken cancellationToken = default) => Fail ? Task.FromException(new IOException("模拟失败")) : Task.CompletedTask;
        public Task DeleteAsync(WorkspaceId id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReplaceActiveNotesAsync(WorkspaceId id, IReadOnlyCollection<Note> notes, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
