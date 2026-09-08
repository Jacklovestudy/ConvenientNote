using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using ConvenientNote.ColorPicker.Application;
using ConvenientNote.ColorPicker.Domain;

namespace ConvenientNote.ColorPicker.UI;

public sealed class ColorSwatch(ColorValue value)
{
    public ColorValue Value { get; } = value;
    public string Hex => Value.Hex;
    public string Rgb => Value.Rgb;
    public SolidColorBrush Brush { get; } = CreateBrush(value);
    private static SolidColorBrush CreateBrush(ColorValue value)
    {
        var brush = new SolidColorBrush(Color.FromRgb(value.R, value.G, value.B));
        brush.Freeze(); return brush;
    }
}

public sealed class ColorPickerViewModel : INotifyPropertyChanged
{
    private readonly ColorPickerService _service;
    private ColorSwatch _current;
    private string _status = "单击“屏幕取色”，再点击屏幕上的任意颜色。";
    public ObservableCollection<ColorSwatch> RecentColors { get; } = [];
    public ColorSwatch Current { get => _current; private set { _current = value; Changed(); } }
    public bool IsHistoryEmpty => RecentColors.Count == 0;
    public string? HistoryWarning => _service.HistoryWarning;
    public bool HasHistoryWarning => !string.IsNullOrEmpty(HistoryWarning);
    public string Status { get => _status; set { _status = value; Changed(); } }

    public ColorPickerViewModel(ColorPickerService service)
    {
        _service = service;
        _current = new ColorSwatch(service.History.FirstOrDefault(new ColorValue(63, 81, 181)));
        RefreshHistory();
    }

    public void Select(ColorValue color, bool remember)
    {
        Current = new ColorSwatch(color);
        if (remember && _service.CanSaveHistory)
        {
            _service.Remember(color);
            RefreshHistory();
        }
        Status = remember
            ? _service.CanSaveHistory ? $"已取色 {color.Hex}，已加入最近颜色。" : $"已取色 {color.Hex}，可以复制；最近颜色暂时只读。"
            : $"已选择 {color.Hex}。";
    }

    private void RefreshHistory()
    {
        RecentColors.Clear();
        foreach (var color in _service.History) RecentColors.Add(new ColorSwatch(color));
        Changed(nameof(IsHistoryEmpty));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
