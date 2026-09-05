using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;

namespace ConvenientNote.Views;

public partial class RichNoteEditorControl
{
    private int _zoomWheelRemainder;
    internal int EditorZoomPercent { get; private set; } = 100;

    private void EditorZoomViewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ApplyZoomWheel(e.Delta, Keyboard.Modifiers, e.GetPosition(EditorZoomViewport)))
            e.Handled = true;
    }

    internal bool ApplyZoomWheel(int delta, ModifierKeys modifiers, Point viewportPoint)
    {
        if (modifiers != ModifierKeys.Control)
        {
            _zoomWheelRemainder = 0;
            return false;
        }
        var accumulated = (long)_zoomWheelRemainder + delta;
        var steps = accumulated / Mouse.MouseWheelDeltaForOneLine;
        _zoomWheelRemainder = (int)(accumulated % Mouse.MouseWheelDeltaForOneLine);
        var percent = (int)Math.Clamp(EditorZoomPercent + steps * 10, 50L, 200L);
        SetEditorZoom(percent, viewportPoint);
        return true;
    }

    private void SetEditorZoom(int percent, Point viewportPoint)
    {
        if (percent == EditorZoomPercent) return;
        var anchor = Editor.IsLoaded
            ? Editor.GetPositionFromPoint(EditorZoomViewport.TranslatePoint(viewportPoint, Editor), true)
            : null;
        EditorZoomPercent = percent;
        EditorZoomTransform.ScaleX = percent / 100d;
        EditorZoomTransform.ScaleY = percent / 100d;
        ResetZoomButton.Content = $"{percent}%";
        if (Editor.IsLoaded)
        {
            EditorZoomViewport.UpdateLayout();
            if (anchor is not null)
            {
                var rect = anchor.GetCharacterRect(LogicalDirection.Forward);
                var position = EditorZoomViewport.TranslatePoint(viewportPoint, Editor);
                if (!rect.IsEmpty) Editor.ScrollToVerticalOffset(Math.Max(0, Editor.VerticalOffset + rect.Top - position.Y));
            }
            UpdateHeadingPositions();
        }
    }

    private void ResetZoomButton_Click(object sender, RoutedEventArgs e)
    {
        _zoomWheelRemainder = 0;
        SetEditorZoom(100, new Point(EditorZoomViewport.ActualWidth / 2, EditorZoomViewport.ActualHeight / 2));
    }
}
