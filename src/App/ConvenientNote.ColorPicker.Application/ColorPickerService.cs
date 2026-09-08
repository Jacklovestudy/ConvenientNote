using ConvenientNote.ColorPicker.Contracts;
using ConvenientNote.ColorPicker.Domain;

namespace ConvenientNote.ColorPicker.Application;

public interface IColorHistoryStore
{
    string? LoadWarning => null;
    bool CanSave => true;
    IReadOnlyList<ColorValue> Load();
    void Save(IReadOnlyList<ColorValue> colors);
}

public sealed class ColorPickerService(IColorHistoryStore store) : IColorHistoryQuery
{
    public const int HistoryLimit = 32;
    private IReadOnlyList<ColorValue> _history = Array.AsReadOnly(store.Load().Distinct().Take(HistoryLimit).ToArray());
    public IReadOnlyList<ColorValue> History => _history;
    public string? HistoryWarning => store.LoadWarning;
    public bool CanSaveHistory => store.CanSave;

    public void Remember(ColorValue color)
    {
        var next = new[] { color }.Concat(_history.Where(x => x != color)).Take(HistoryLimit).ToArray();
        // Publish only after durable persistence succeeds.
        store.Save(next);
        _history = Array.AsReadOnly(next);
    }

    public IReadOnlyList<ColorDto> GetRecentColors() => _history.Select(x => new ColorDto(x.Hex, x.Rgb)).ToArray();
}
