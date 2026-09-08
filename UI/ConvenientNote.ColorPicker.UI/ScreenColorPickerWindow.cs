using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConvenientNote.ColorPicker.Application;
using ConvenientNote.ColorPicker.Domain;

namespace ConvenientNote.ColorPicker.UI;

/// <summary>Shows a frozen desktop and samples its original physical pixels.</summary>
public sealed class ScreenColorPickerWindow : Window
{
    private readonly ScreenSnapshot _snapshot;
    private readonly IScreenCapture _capture;
    private readonly Canvas _canvas = new();
    private readonly Border _preview;
    private readonly Border _swatch;
    private readonly TextBlock _label;
    public ColorValue? SelectedColor { get; private set; }

    public static ColorValue? Pick(Window? owner, ScreenSnapshot snapshot, IScreenCapture capture)
    {
        // Create the HWND in a per-monitor DPI context even when the host uses system DPI.
        var previous = SetThreadDpiAwarenessContext(new nint(-4));
        try
        {
            var picker = new ScreenColorPickerWindow(snapshot, capture) { Owner = owner };
            return picker.ShowDialog() == true ? picker.SelectedColor : null;
        }
        finally { if (previous != 0) SetThreadDpiAwarenessContext(previous); }
    }

    public ScreenColorPickerWindow(ScreenSnapshot snapshot, IScreenCapture capture)
    {
        _snapshot = snapshot; _capture = capture;
        Title = "屏幕取色 · 单击确认，Esc 取消";
        WindowStyle = WindowStyle.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = Cursors.Cross;
        Background = Brushes.Black;
        var bitmap = BitmapSource.Create(snapshot.Width, snapshot.Height, 96, 96,
            PixelFormats.Bgr32, null, snapshot.CopyPixels(), snapshot.Stride);
        bitmap.Freeze();
        var image = new Image { Source = bitmap, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var root = new Grid();
        root.Children.Add(image);
        root.Children.Add(_canvas);
        _canvas.IsHitTestVisible = false;
        _swatch = new Border { Width = 36, Height = 36, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 12, 0) };
        _label = new TextBlock { Foreground = Brushes.White, FontSize = 14, FontFamily = new FontFamily("Segoe UI") };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_swatch); row.Children.Add(_label);
        _preview = new Border { Background = new SolidColorBrush(Color.FromRgb(25, 28, 36)), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Child = row };
        _canvas.Children.Add(_preview);
        Content = root;
        SourceInitialized += (_, _) => PlaceWindow();
        Loaded += (_, _) => { PlaceWindow(); Activate(); Focus(); UpdatePreview(); };
        MouseMove += (_, _) => UpdatePreview();
        MouseLeftButtonDown += (sender, e) =>
        {
            if (TrySample(out var color, out _)) { SelectedColor = color; DialogResult = true; }
            e.Handled = true;
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; DialogResult = false; } };
        // Alt+Tab cancels rather than leaving a topmost screenshot over another application.
        Deactivated += (_, _) => { if (IsVisible) Close(); };
    }

    private void PlaceWindow()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (!SetWindowPos(handle, new nint(-1), _snapshot.Left, _snapshot.Top, _snapshot.Width, _snapshot.Height, 0x0040))
            throw new System.ComponentModel.Win32Exception();
    }

    private bool TrySample(out ColorValue color, out Point local)
    {
        int x, y;
        try { (x, y) = _capture.GetCursorPosition(); }
        catch (System.ComponentModel.Win32Exception)
        {
            color = default; local = default;
            _label.Text = "当前无法读取鼠标位置，请按 Esc 退出后重试。";
            return false;
        }
        var relativeX = (long)x - _snapshot.Left;
        var relativeY = (long)y - _snapshot.Top;
        local = new Point(relativeX * ActualWidth / _snapshot.Width, relativeY * ActualHeight / _snapshot.Height);
        if (relativeX < 0 || relativeY < 0 || relativeX >= _snapshot.Width || relativeY >= _snapshot.Height)
        { color = default; return false; }
        color = _snapshot.Sample(x, y);
        return true;
    }

    private void UpdatePreview()
    {
        if (!TrySample(out var color, out var point)) return;
        _swatch.Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        _label.Text = $"{color.Hex}  ·  {color.Rgb}\n单击确认  ·  Esc 取消";
        _preview.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _preview.DesiredSize;
        Canvas.SetLeft(_preview, Math.Clamp(point.X + 22, 0, Math.Max(0, ActualWidth - size.Width)));
        Canvas.SetTop(_preview, Math.Clamp(point.Y + 22, 0, Math.Max(0, ActualHeight - size.Height)));
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint context);
}
