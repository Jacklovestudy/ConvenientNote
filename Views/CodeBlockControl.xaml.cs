using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ConvenientNote.Services;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Indentation.CSharp;

namespace ConvenientNote.Views;

public partial class CodeBlockControl : UserControl
{
    private bool _updating = true;
    private readonly DispatcherTimer _copyFeedbackTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    internal CodeBlock? Block { get; private set; }
    public event EventHandler? ContentEdited;
    public event EventHandler? ExitRequested;

    public CodeBlockControl()
    {
        InitializeComponent();
        CodeEditor.Options.ConvertTabsToSpaces = true;
        CodeEditor.Options.IndentationSize = 4;
        _copyFeedbackTimer.Tick += (_, _) => { _copyFeedbackTimer.Stop(); CopyCodeButton.Content = "复制代码"; };
        Unloaded += (_, _) => _copyFeedbackTimer.Stop();
    }

    internal void Attach(CodeBlock block)
    {
        _updating = true;
        Block = block;
        CodeEditor.Text = block.CodeText;
        CodeEditor.Document.UndoStack.ClearAll();
        LanguageBox.SelectedIndex = block.CodeLanguage == "C#" ? 0 : 1;
        WrapCheckBox.IsChecked = block.WrapCode;
        ApplyPresentation();
        _updating = false;
    }

    private void ApplyPresentation()
    {
        if (Block is null) return;
        CodeEditor.SyntaxHighlighting = Block.CodeLanguage == "C#" ? HighlightingManager.Instance.GetDefinition("C#") : null;
        CodeEditor.TextArea.IndentationStrategy = Block.CodeLanguage == "C#"
            ? new CSharpIndentationStrategy(CodeEditor.Options)
            : new ICSharpCode.AvalonEdit.Indentation.DefaultIndentationStrategy();
        CodeEditor.WordWrap = Block.WrapCode;
        CodeEditor.HorizontalScrollBarVisibility = Block.WrapCode ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        UpdateHeight();
    }

    private void UpdateHeight() => CodeEditor.Height = Math.Clamp(CodeEditor.Document.LineCount * 21 + 20, 84, 380);

    private void CodeEditor_TextChanged(object? sender, EventArgs e)
    {
        if (_updating || Block is null) return;
        Block.CodeText = CodeEditor.Text;
        UpdateHeight();
        ContentEdited?.Invoke(this, EventArgs.Empty);
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || Block is null) return;
        Block.CodeLanguage = LanguageBox.SelectedIndex == 0 ? "C#" : "Text";
        ApplyPresentation();
        ContentEdited?.Invoke(this, EventArgs.Empty);
    }

    private void WrapCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_updating || Block is null) return;
        Block.WrapCode = WrapCheckBox.IsChecked == true;
        ApplyPresentation();
        ContentEdited?.Invoke(this, EventArgs.Empty);
    }

    private void CopyCodeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(Block?.CodeText)) { CopyCodeButton.Content = "代码为空"; }
            else { Clipboard.SetText(Block.CodeText); CopyCodeButton.Content = "已复制"; }
        }
        catch (ExternalException) { CopyCodeButton.Content = "复制失败，请重试"; }
        _copyFeedbackTimer.Stop(); _copyFeedbackTimer.Start();
    }

    private void CodeEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control))
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    internal void SelectCode(int offset, int length)
    {
        CodeEditor.Focus();
        CodeEditor.Select(offset, length);
        var location = CodeEditor.Document.GetLocation(offset);
        CodeEditor.ScrollTo(location.Line, location.Column);
    }

    internal void UndoCode() { if (CodeEditor.CanUndo) CodeEditor.Undo(); CodeEditor.Focus(); }
    internal void RedoCode() { if (CodeEditor.CanRedo) CodeEditor.Redo(); CodeEditor.Focus(); }
}
