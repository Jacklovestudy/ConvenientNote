using System.Windows.Documents;
using System.Windows;

namespace ConvenientNote.Services;

/// <summary>
/// Presentation placeholder. HiddenDocument is an independent snapshot, so removing
/// the original blocks remains a normal, reversible RichTextBox editing operation.
/// Persistence enumerates the snapshot, never the placeholder's button.
/// </summary>
public sealed class FoldedSection : BlockUIContainer
{
    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(
        nameof(Heading), typeof(Paragraph), typeof(FoldedSection));
    public static readonly DependencyProperty HiddenDocumentProperty = DependencyProperty.Register(
        nameof(HiddenDocument), typeof(FlowDocument), typeof(FoldedSection));

    // WPF undo reconstructs TextElements using a public default constructor and
    // restores their local dependency properties, rather than their CLR fields.
    public FoldedSection() { HiddenDocument = new FlowDocument(); }

    public FoldedSection(Paragraph heading, FlowDocument hiddenDocument)
    {
        Heading = heading;
        HiddenDocument = hiddenDocument;
    }

    public Paragraph Heading { get => (Paragraph)GetValue(HeadingProperty); internal set => SetValue(HeadingProperty, value); }
    public FlowDocument HiddenDocument { get => (FlowDocument)GetValue(HiddenDocumentProperty); private set => SetValue(HiddenDocumentProperty, value); }
}
