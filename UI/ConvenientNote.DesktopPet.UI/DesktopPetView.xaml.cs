using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ConvenientNote.DesktopPet.Domain;

namespace ConvenientNote.DesktopPet.UI;

public partial class DesktopPetView : UserControl
{
    private readonly PetController _controller;
    private readonly PetMotion _motion = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _saveSize = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer _saveSpeed = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Stopwatch _clock = new();
    private double _last, _phase;

    public DesktopPetView(PetController controller)
    {
        _controller = controller;
        InitializeComponent();
        DataContext = controller;
        _timer.Tick += (_, _) =>
        {
            var now = _clock.Elapsed.TotalSeconds;
            var dt = Math.Clamp(now - _last, 0, .1); _last = now;
            _motion.RidingSpeed = _controller.RidingSpeed;
            _motion.Advance(dt);
            _phase += dt * (_motion.Speed > 0 ? _motion.Speed / 10 : .8);
            Preview.Update(_motion.Action, _phase, _motion.Age, _motion.Direction);
        };
        _saveSize.Tick += (_, _) => { _saveSize.Stop(); _controller.SetScale(SizeSlider.Value); };
        _saveSpeed.Tick += (_, _) => { _saveSpeed.Stop(); _controller.SetRidingSpeed(SpeedSlider.Value); };
        Loaded += (_, _) => { _clock.Restart(); _last = 0; _timer.Start(); };
        Unloaded += (_, _) =>
        {
            _timer.Stop(); _clock.Stop();
            if (_saveSize.IsEnabled) { _saveSize.Stop(); _controller.SetScale(SizeSlider.Value); }
            if (_saveSpeed.IsEnabled) { _saveSpeed.Stop(); _controller.SetRidingSpeed(SpeedSlider.Value); }
        };
    }

    private void TogglePet(object sender, RoutedEventArgs e) { if (_controller.IsVisible) _controller.Hide(); else _controller.Show(); }
    private void ToggleRoaming(object sender, RoutedEventArgs e) => _controller.SetRoaming(((CheckBox)sender).IsChecked == true);
    private void PetSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        _saveSize.Stop(); _saveSize.Start();
    }
    private void Play(object sender, RoutedEventArgs e)
    {
        switch (((Button)sender).Tag as string)
        {
            case "Ride": _motion.Ride(); break;
            case "Boost": _motion.Boost(); break;
            case "Brake": _motion.Ride(); _motion.Brake(); break;
            case "React": _motion.React(); break;
            case "Drag": _motion.BeginDrag(); break;
            case "Sleep": _motion.Sleep(); break;
            case "Crash": _motion.Ride(); _motion.TurnAtEdge(); break;
        }
    }

    private void RidingSpeedChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        _saveSpeed.Stop(); _saveSpeed.Start();
    }
}
