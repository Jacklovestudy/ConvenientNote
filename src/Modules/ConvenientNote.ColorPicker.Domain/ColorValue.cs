using System.Globalization;

namespace ConvenientNote.ColorPicker.Domain;

/// <summary>An opaque sRGB color, independent of platform and presentation types.</summary>
public readonly record struct ColorValue(byte R, byte G, byte B)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    public string Rgb => FormattableString.Invariant($"rgb({R}, {G}, {B})");

    public static ColorValue ParseHex(string value)
    {
        if (value is null || value.Length != 7 || value[0] != '#' ||
            !uint.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var number))
            throw new FormatException("颜色必须是 #RRGGBB 格式。");
        return new((byte)(number >> 16), (byte)(number >> 8), (byte)number);
    }
}
