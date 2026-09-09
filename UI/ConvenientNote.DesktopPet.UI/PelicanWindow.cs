using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ConvenientNote.DesktopPet.Domain;

namespace ConvenientNote.DesktopPet.UI;

public sealed class PelicanWindow : Window
{
    private readonly PelicanVisual _visual = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _clickTimer = new() { Interval = TimeSpan.FromMilliseconds(260) };
    private readonly Stopwatch _clock = new();
    private double _last, _phase;
    private Point? _press, _offset;
    private bool _dragging, _closing;
    private HwndSource? _source;
    public PetMotion Motion { get; } = new();
    public bool Roaming { get; set; }
    public event EventHandler? PositionSettled;
    public PelicanVisual Artwork => _visual;

    public PelicanWindow(PetPreferences preferences, Action hide, Action openSettings, Action toggleRoaming)
    {
        Title = "骑行鹈鹕";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Roaming = preferences.Roaming;
        if (!Roaming) Motion.Sleep();
        Width = 260 * preferences.Scale;
        Height = Width * 310 / 360;
        Left = preferences.Left ?? SystemParameters.WorkArea.Right - Width - 24;
        Top = preferences.Top ?? SystemParameters.WorkArea.Bottom - Height - 8;
        Content = _visual;
        AutomationProperties.SetName(_visual, "骑行鹈鹕：单击歪头，双击加速，按住拖动，右键设置");
        _visual.ToolTip = "单击歪头 · 双击加速 · 按住拖动 · 右键设置";
        var menu = new ContextMenu();
        AddMenu(menu, "骑一会儿", () => Motion.Ride());
        AddMenu(menu, "打个盹", () => Motion.Sleep());
        AddMenu(menu, "切换自动骑行", toggleRoaming);
        menu.Items.Add(new Separator());
        AddMenu(menu, "桌宠设置", openSettings);
        AddMenu(menu, "隐藏鹈鹕", hide);
        _visual.ContextMenu = menu;
        _timer.Tick += Tick;
        _clickTimer.Tick += (_, _) => { _clickTimer.Stop(); Motion.React(); };
        Loaded += (_, _) => { DesktopGeometry.Clamp(this); _clock.Start(); _last = _clock.Elapsed.TotalSeconds; _timer.Start(); };
        _visual.MouseLeftButtonDown += Press;
        _visual.MouseMove += Move;
        _visual.MouseLeftButtonUp += Release;
        _visual.LostMouseCapture += (_, _) => EndDrag();
    }

    private static void AddMenu(ContextMenu menu, string text, Action action)
    {
        var item = new MenuItem { Header = text };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _source = (HwndSource)PresentationSource.FromVisual(this);
        _source.AddHook(WindowMessage);
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x08000000 | 0x00000080);
    }

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return 3; } // Mouse interaction without activating the window.
        if (message is 0x007E or 0x02E0 && !_closing)
            Dispatcher.BeginInvoke(new Action(() => { if (!_closing) DesktopGeometry.Clamp(this); }));
        return 0;
    }

    private void Press(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _clickTimer.Stop();
        if (e.ClickCount == 2) { Motion.Boost(); _press = null; return; }
        if (DesktopGeometry.Cursor(this) is not { } cursor) return;
        _press = cursor;
        _offset = new Point(cursor.X - Left, cursor.Y - Top);
        _visual.CaptureMouse();
    }

    private void Move(object sender, MouseEventArgs e)
    {
        if (_press is not { } origin || _offset is not { } offset || e.LeftButton != MouseButtonState.Pressed) return;
        if (DesktopGeometry.Cursor(this) is not { } cursor) return;
        if (!_dragging && (cursor - origin).Length > 4) { _dragging = true; Motion.BeginDrag(); }
        if (!_dragging) return;
        Left = cursor.X - offset.X;
        Top = cursor.Y - offset.Y;
    }

    private void Release(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var wasPressed = _press.HasValue;
        var wasDragging = _dragging;
        EndDrag();
        _visual.ReleaseMouseCapture();
        if (wasPressed && !wasDragging) _clickTimer.Start();
    }

    private void EndDrag()
    {
        _press = null; _offset = null;
        if (!_dragging) return;
        _dragging = false;
        Motion.Release();
        DesktopGeometry.Clamp(this);
        PositionSettled?.Invoke(this, EventArgs.Empty);
    }

    public void ResizePet(double scale)
    {
        var bottom = Top + Height;
        Width = 260 * scale; Height = Width * 310 / 360;
        Top = bottom - Height;
        if (IsLoaded) DesktopGeometry.Clamp(this);
    }

    private void Tick(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var delta = Math.Clamp(now - _last, 0, .1); _last = now;
        if (_visual.ContextMenu?.IsOpen == true || _press.HasValue && !_dragging) return;
        Motion.Advance(delta);
        var speed = Motion.Speed;
        _phase += delta * (speed > 0 ? speed / 10 : .8);
        if (Roaming && !_dragging && speed > 0)
        {
            var area = DesktopGeometry.WorkArea(this);
            var next = Left + Motion.Direction * speed * delta;
            var max = Math.Max(area.Left, area.Right - Width);
            Left = Math.Clamp(next, area.Left, max);
            if (next <= area.Left || next >= max) Motion.TurnAtEdge();
        }
        _visual.Update(Motion.Action, _phase, Motion.Age, Motion.Direction);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closing = true;
        _timer.Stop(); _clickTimer.Stop(); _clock.Stop();
        _timer.Tick -= Tick;
        if (_visual.ContextMenu is { } menu) menu.IsOpen = false;
        _source?.RemoveHook(WindowMessage);
        _source = null;
        base.OnClosed(e);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(nint window, int index, int value);
}
