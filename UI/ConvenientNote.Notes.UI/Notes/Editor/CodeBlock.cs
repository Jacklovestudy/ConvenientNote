using System.Windows;
using System.Windows.Documents;

namespace ConvenientNote.Services;

/// <summary>Persisted code content; the editor view is attached by the owning window.</summary>
public sealed class CodeBlock : BlockUIContainer
{
    public static readonly DependencyProperty CodeTextProperty = DependencyProperty.Register(
        nameof(CodeText), typeof(string), typeof(CodeBlock), new FrameworkPropertyMetadata(string.Empty));
    public static readonly DependencyProperty CodeLanguageProperty = DependencyProperty.Register(
        nameof(CodeLanguage), typeof(string), typeof(CodeBlock), new FrameworkPropertyMetadata("C#"));
    public static readonly DependencyProperty WrapCodeProperty = DependencyProperty.Register(
        nameof(WrapCode), typeof(bool), typeof(CodeBlock), new FrameworkPropertyMetadata(true));

    // WPF undo reconstructs the element and restores its dependency properties.
    public CodeBlock() { }
    public string CodeText { get => (string)GetValue(CodeTextProperty); set => SetValue(CodeTextProperty, value); }
    public string CodeLanguage { get => (string)GetValue(CodeLanguageProperty); set => SetValue(CodeLanguageProperty, value); }
    public bool WrapCode { get => (bool)GetValue(WrapCodeProperty); set => SetValue(WrapCodeProperty, value); }
}
