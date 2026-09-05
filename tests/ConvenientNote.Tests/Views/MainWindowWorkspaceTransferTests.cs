using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Markup;
using Xunit;

namespace ConvenientNote.Tests.Views;

public sealed class MainWindowWorkspaceTransferTests
{
    [Theory]
    [InlineData("DayTodoView.xaml")]
    [InlineData("InboxView.xaml")]
    [InlineData("CompletedTodoView.xaml")]
    public void TodoWrapperRegistersNoObsoleteReplacementParticipantName(string fileName)
    {
        RunSta(() =>
        {
            var wrapper = LoadViewMarkup(Path.Combine("Views", fileName));

            Assert.Null(wrapper.FindName("TodoBoard"));
        });
    }

    [Fact]
    public void DrawerContainsNoImportOrExportActions()
    {
        RunSta(() =>
        {
            var window = LoadMainWindowMarkup();

            Assert.Null(window.FindName("WorkspaceTransferActionsPanel"));
            Assert.Null(window.FindName("ExportWorkspaceButton"));
            Assert.Null(window.FindName("ImportWorkspaceButton"));
            Assert.DoesNotContain(
                FindDescendants<Button>(window),
                button => AutomationProperties.GetName(button) is "导出数据" or "导入数据");
        });
    }

    private static Window LoadMainWindowMarkup()
    {
        var markup = File.ReadAllText(FindSourceFile("MainWindow.xaml"));
        markup = markup.Replace(
            "xmlns:local=\"clr-namespace:ConvenientNote\"",
            "xmlns:local=\"clr-namespace:ConvenientNote;assembly=ConvenientNote\"");
        markup = markup.Replace(
            "xmlns:views=\"clr-namespace:ConvenientNote.Views\"",
            "xmlns:views=\"clr-namespace:ConvenientNote.Views;assembly=ConvenientNote\"");
        markup = markup.Replace("Style=\"{StaticResource TitleBarButtonStyle}\"", string.Empty)
            .Replace("Style=\"{StaticResource TitleBarCloseButtonStyle}\"", string.Empty);
        markup = Regex.Replace(
            markup,
            "\\s+(?:x:Class|prism:ViewModelLocator.AutoWireViewModel|prism:RegionManager.RegionName|Loaded|Closing|Click|PreviewKeyDown|PreviewMouseLeftButtonDown|Handler|Icon)=\"[^\"]*\"",
            string.Empty);
        return Assert.IsType<Window>(XamlReader.Parse(markup));
    }

    private static UserControl LoadViewMarkup(string relativePath)
    {
        var markup = File.ReadAllText(FindSourceFile(relativePath));
        markup = markup.Replace(
            "xmlns:views=\"clr-namespace:ConvenientNote.Views\"",
            "xmlns:views=\"clr-namespace:ConvenientNote.Views;assembly=ConvenientNote\"");
        markup = Regex.Replace(
            markup,
            "\\s+(?:x:Class|prism:ViewModelLocator.AutoWireViewModel)=\"[^\"]*\"",
            string.Empty);
        return Assert.IsType<UserControl>(XamlReader.Parse(markup));
    }

    private static string FindSourceFile(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate {fileName} from the test output directory.");
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
