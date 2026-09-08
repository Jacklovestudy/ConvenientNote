using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ConvenientNote.UI.Common;

public partial class MaterialDialogWindow : Window
{
    public MaterialDialogWindow(string title, Window? owner = null)
    {
        InitializeComponent();
        Title = title;
        if (owner is { IsVisible: true }) Owner = owner;
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; DialogResult = false; }
        };
    }

    public UIElement DialogContent { set => Body.Content = value; }

    private void Dismiss(object sender, RoutedEventArgs e) => DialogResult = false;

    public static bool Confirm(Window? owner, string title, string message, string confirmLabel = "确定")
        => Show(owner, title, message, confirmLabel, true);

    public static void Inform(Window? owner, string title, string message)
        => Show(owner, title, message, "知道了", false);

    private static bool Show(Window? owner, string title, string message, string confirmLabel, bool cancellable)
    {
        var window = new MaterialDialogWindow(title, owner);
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(76, 86, 105)), LineHeight = 24
        });
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 24, 0, 0)
        };
        if (cancellable)
        {
            var cancel = new Button { Content = "取消", IsCancel = true, IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 12, 0) };
            cancel.SetResourceReference(StyleProperty, "MaterialDesignFlatButton");
            cancel.Click += (_, _) => window.DialogResult = false;
            actions.Children.Add(cancel);
            window.Loaded += (_, _) => cancel.Focus();
        }
        var confirm = new Button { Content = confirmLabel, IsDefault = !cancellable, MinWidth = 96 };
        confirm.SetResourceReference(StyleProperty, "MaterialDesignRaisedButton");
        confirm.Click += (_, _) => window.DialogResult = true;
        actions.Children.Add(confirm);
        body.Children.Add(actions);
        window.DialogContent = body;
        return window.ShowDialog() == true;
    }
}
