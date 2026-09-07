using System.Windows.Documents;
using System.Windows;
using System.Windows.Media;

namespace ConvenientNote.Services;

public sealed class InlineCode : Span
{
    public static readonly DependencyProperty PreviousFontFamilyProperty = DependencyProperty.RegisterAttached(
        "PreviousFontFamily", typeof(FontFamily), typeof(InlineCode));
    public static FontFamily? GetPreviousFontFamily(DependencyObject target) => (FontFamily?)target.GetValue(PreviousFontFamilyProperty);
    public static void SetPreviousFontFamily(DependencyObject target, FontFamily? value) => target.SetValue(PreviousFontFamilyProperty, value);
    public InlineCode() => ApplyAppearance();
    public InlineCode(Inline inline) : base(inline) => ApplyAppearance();
    public InlineCode(TextPointer start, TextPointer end) : base(start, end) => ApplyAppearance();

    private void ApplyAppearance()
    {
        FontFamily = new FontFamily("Consolas");
        Background = new SolidColorBrush(Color.FromArgb(30, 100, 116, 139));
        foreach (var inline in Descendants(Inlines))
        {
            if (inline.ReadLocalValue(FontFamilyProperty) is FontFamily previous)
            {
                SetPreviousFontFamily(inline, previous);
                inline.ClearValue(FontFamilyProperty);
            }
        }
    }

    public void RestoreFontFamilies()
    {
        foreach (var inline in Descendants(Inlines))
        {
            if (GetPreviousFontFamily(inline) is { } previous) inline.FontFamily = previous;
            inline.ClearValue(PreviousFontFamilyProperty);
        }
    }

    private static IEnumerable<Inline> Descendants(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            yield return inline;
            if (inline is Span span)
                foreach (var child in Descendants(span.Inlines)) yield return child;
        }
    }
}
