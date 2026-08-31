using System.Windows;
using System.Windows.Documents;
using System.Globalization;

namespace ConvenientNote.Services;

public static class ParagraphLineSpacing
{
    private const double NaturalLineHeightFactor = 1.35;
    private static readonly DependencyProperty RatioProperty = DependencyProperty.RegisterAttached(
        "Ratio",
        typeof(double),
        typeof(ParagraphLineSpacing),
        new FrameworkPropertyMetadata(1d));

    public static double GetRatio(Paragraph paragraph) => (double)paragraph.GetValue(RatioProperty);

    public static bool TryParseRatio(string? text, out double ratio)
    {
        var normalized = (text ?? string.Empty)
            .Replace("行距", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim()
            .Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out ratio)
            && ratio is >= 0.8 and <= 3;
    }

    public static void Apply(Paragraph paragraph, double ratio)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ratio = Math.Clamp(ratio, 0.8, 3d);
        paragraph.SetValue(RatioProperty, ratio);
        paragraph.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        paragraph.LineHeight = GetMaximumFontSize(paragraph) * NaturalLineHeightFactor * ratio;
    }

    public static void Refresh(Paragraph paragraph) => Apply(paragraph, GetRatio(paragraph));

    private static double GetMaximumFontSize(Paragraph paragraph)
    {
        var maximum = paragraph.FontSize;
        foreach (var inline in paragraph.Inlines)
        {
            maximum = Math.Max(maximum, GetMaximumFontSize(inline));
        }

        return maximum;
    }

    private static double GetMaximumFontSize(Inline inline)
    {
        var maximum = inline.FontSize;
        if (inline is Span span)
        {
            foreach (var child in span.Inlines)
            {
                maximum = Math.Max(maximum, GetMaximumFontSize(child));
            }
        }

        return maximum;
    }
}
