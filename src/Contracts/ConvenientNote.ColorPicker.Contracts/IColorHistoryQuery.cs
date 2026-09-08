namespace ConvenientNote.ColorPicker.Contracts;

public sealed record ColorDto(string Hex, string Rgb);

/// <summary>Read-only integration contract. Consumers do not receive domain objects.</summary>
public interface IColorHistoryQuery
{
    IReadOnlyList<ColorDto> GetRecentColors();
}
