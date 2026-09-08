using System.Windows;
using System.Windows.Controls;
using ConvenientNote.ColorPicker.Application;

namespace ConvenientNote.ColorPicker.UI;

public partial class ColorPickerView : UserControl
{
    private readonly IScreenCapture _capture;
    private readonly ColorPickerViewModel _viewModel;

    public ColorPickerView(ColorPickerService service, IScreenCapture capture)
    {
        InitializeComponent();
        _capture = capture;
        _viewModel = new ColorPickerViewModel(service);
        DataContext = _viewModel;
    }

    private void PickColor(object sender, RoutedEventArgs e)
    {
        PickButton.IsEnabled = false;
        try
        {
            var snapshot = _capture.Capture();
            if (ScreenColorPickerWindow.Pick(Window.GetWindow(this), snapshot, _capture) is { } color)
                _viewModel.Select(color, remember: true);
            else _viewModel.Status = "已取消取色。";
        }
        catch (Exception error)
        {
            _viewModel.Status = $"取色或保存失败：{error.Message}";
        }
        finally { PickButton.IsEnabled = true; PickButton.Focus(); }
    }

    private void CopyHex(object sender, RoutedEventArgs e) => Copy(_viewModel.Current.Hex);
    private void CopyRgb(object sender, RoutedEventArgs e) => Copy(_viewModel.Current.Rgb);
    private void Copy(string value)
    {
        try { Clipboard.SetText(value); _viewModel.Status = $"已复制 {value}。"; }
        catch (System.Runtime.InteropServices.ExternalException) { _viewModel.Status = "剪贴板暂时被其他程序占用，请重试。"; }
    }

    private void SelectRecent(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ColorSwatch color }) _viewModel.Select(color.Value, remember: false);
    }
}
