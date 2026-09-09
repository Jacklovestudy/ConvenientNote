using System.ComponentModel;
using System.Runtime.CompilerServices;
using ConvenientNote.DesktopPet.Application;
using ConvenientNote.DesktopPet.Contracts;
using ConvenientNote.DesktopPet.Domain;

namespace ConvenientNote.DesktopPet.UI;

public sealed class PetController(PetPreferencesService preferences) : IDesktopPet, INotifyPropertyChanged
{
    private PelicanWindow? _window;
    private string _status = preferences.LoadWarning ?? "先看看动作，喜欢的话就让它到桌面骑一会儿。";
    public bool IsVisible => _window?.IsVisible == true;
    public string ToggleLabel => IsVisible ? "隐藏桌宠" : "显示桌宠";
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public double Scale => preferences.Current.Scale;
    public bool Roaming => preferences.Current.Roaming;
    public event Action? OpenSettings;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void Restore() { if (preferences.Current.Enabled) Show(); }
    public void Show()
    {
        if (_window is not null) return;
        try
        {
            preferences.Update(preferences.Current with { Enabled = true });
            var window = new PelicanWindow(preferences.Current, Hide, () => OpenSettings?.Invoke(), () => SetRoaming(!Roaming));
            _window = window;
            window.PositionSettled += SavePosition;
            // WPF closes secondary windows before App.OnExit; persist before that close.
            window.Closing += (_, _) => Save(preferences.Current.Enabled);
            window.Closed += (_, _) => { if (ReferenceEquals(_window, window)) _window = null; Refresh(); };
            window.Show();
            Status = "鹈鹕已到桌面：单击歪头，双击加速，右键打开菜单。";
        }
        catch (Exception error)
        {
            var failedWindow = _window; _window = null;
            failedWindow?.Close();
            Status = "无法显示桌宠：" + error.Message;
        }
        Refresh();
    }

    public void Hide()
    {
        Save(false);
        _window?.Close();
        Refresh();
    }

    public void Shutdown()
    {
        if (_window is null) return;
        Save(preferences.Current.Enabled);
        _window.Close();
    }

    public void SetScale(double value)
    {
        try { preferences.Update(preferences.Current with { Scale = Math.Round(value, 2) }); _window?.ResizePet(Scale); }
        catch (Exception error) { Status = "大小未保存：" + error.Message; }
        Changed(nameof(Scale));
    }

    public void SetRoaming(bool value)
    {
        try
        {
            preferences.Update(preferences.Current with { Roaming = value });
            if (_window is not null) { _window.Roaming = value; if (value) _window.Motion.Ride(); else _window.Motion.Sleep(); }
        }
        catch (Exception error) { Status = "设置未保存：" + error.Message; }
        Changed(nameof(Roaming));
    }

    private void SavePosition(object? sender, EventArgs e) => Save(preferences.Current.Enabled);
    private void Save(bool enabled)
    {
        try
        {
            preferences.Update(preferences.Current with { Enabled = enabled, Left = _window?.Left ?? preferences.Current.Left, Top = _window?.Top ?? preferences.Current.Top });
            Status = enabled ? "桌宠位置已记住。" : "鹈鹕休息了，随时可以再次显示。";
        }
        catch (Exception error) { Status = "桌宠设置未保存：" + error.Message; }
    }
    private void Refresh() { Changed(nameof(IsVisible)); Changed(nameof(ToggleLabel)); }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
