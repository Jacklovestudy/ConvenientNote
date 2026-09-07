using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ConvenientNote.ViewModels;

namespace ConvenientNote.Views;

public partial class NoteWallControl : UserControl
{
    private Border? _draggedCard;
    private NoteCardViewModel? _draggedNote;
    private Point _pointerStart;
    private double _noteStartX;
    private double _noteStartY;
    private bool _didDrag;

    public NoteWallControl()
    {
        InitializeComponent();
    }

    private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { DataContext: NoteCardViewModel note } card)
        {
            return;
        }
        if (e.ClickCount == 2)
        {
            if (DataContext is NotesViewModel viewModel)
            {
                viewModel.OpenNoteCommand.Execute(note);
                e.Handled = true;
            }
            return;
        }
        _draggedCard = card;
        _draggedNote = note;
        _pointerStart = e.GetPosition(this);
        _noteStartX = note.X;
        _noteStartY = note.Y;
        _didDrag = false;
        card.CaptureMouse();
    }

    private void Card_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedCard is null || _draggedNote is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }
        var current = e.GetPosition(this);
        var delta = current - _pointerStart;
        if (!_didDrag && Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }
        _didDrag = true;
        _draggedNote.MoveTo(_noteStartX + delta.X, _noteStartY + delta.Y);
        e.Handled = true;
    }

    private async void Card_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var note = _draggedNote;
        _draggedCard?.ReleaseMouseCapture();
        _draggedCard = null;
        _draggedNote = null;
        if (_didDrag && note is not null && DataContext is NotesViewModel viewModel)
        {
            await viewModel.MoveNoteAsync(note);
            e.Handled = true;
        }
        _didDrag = false;
    }

    private void OpenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: NoteCardViewModel note } && DataContext is NotesViewModel viewModel)
        {
            viewModel.OpenNoteCommand.Execute(note);
        }
    }

    private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: NoteCardViewModel note } && DataContext is NotesViewModel viewModel)
        {
            viewModel.OpenNoteCommand.Execute(note);
            viewModel.MoveToTrashCommand.Execute();
        }
    }
}
