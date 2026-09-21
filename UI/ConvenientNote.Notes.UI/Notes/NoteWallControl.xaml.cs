using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections.Specialized;
using System.Windows.Threading;
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
    private bool _manualLayout;
    private readonly DispatcherTimer _arrangeTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };

    public NoteWallControl()
    {
        InitializeComponent();
        _arrangeTimer.Tick += (_, _) => { _arrangeTimer.Stop(); AutoArrange(); };
        ((INotifyCollectionChanged)WallItems.Items).CollectionChanged += (_, _) => ScheduleArrange();
        Loaded += (_, _) => AutoArrange();
        Unloaded += (_, _) => _arrangeTimer.Stop();
        WallScrollViewer.SizeChanged += (_, _) => ScheduleArrange();
    }

    private void ScheduleArrange()
    {
        if (!IsLoaded) return;
        _arrangeTimer.Stop();
        _arrangeTimer.Start();
    }

    private void WallScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportWidthChange == 0 && e.ViewportHeightChange == 0) return;
        if (!_manualLayout) { ScheduleArrange(); return; }
        // A scrollbar appearing during a drag must not undo the user's drop.
        foreach (var note in WallItems.Items.OfType<NoteCardViewModel>())
        {
            var width = Math.Min(note.DisplayWidth, Math.Max(1, WallScrollViewer.ViewportWidth - 32));
            note.ArrangeForDisplay(Math.Clamp(note.DisplayX, 0, Math.Max(0, WallScrollViewer.ViewportWidth - width)), note.DisplayY, width);
        }
        UpdateSurfaceHeight();
    }

    public void AutoArrange()
    {
        _arrangeTimer.Stop();
        if (_draggedCard is not null) { ScheduleArrange(); return; }
        var viewport = WallScrollViewer.ViewportWidth;
        if (!double.IsFinite(viewport) || viewport <= 0) return;
        _manualLayout = false;
        const double padding = 16;
        const double gap = 20;
        var availableWidth = Math.Max(1, viewport - padding * 2);
        double x = padding, y = padding, rowHeight = 0;
        foreach (var note in WallItems.Items.OfType<NoteCardViewModel>())
        {
            var width = Math.Min(note.Width, availableWidth);
            if (x > padding && x + width > viewport - padding)
            {
                x = padding;
                y += rowHeight + gap;
                rowHeight = 0;
            }
            note.ArrangeForDisplay(x, y, width);
            rowHeight = Math.Max(rowHeight, note.Height);
            x += width + gap;
        }
        UpdateSurfaceHeight();
    }

    private void UpdateSurfaceHeight()
    {
        var bottom = WallItems.Items.OfType<NoteCardViewModel>()
            .Select(note => note.DisplayY + note.Height + 16).DefaultIfEmpty(0).Max();
        WallSurface.Height = Math.Max(WallScrollViewer.ViewportHeight, bottom);
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
        _noteStartX = note.DisplayX;
        _noteStartY = note.DisplayY;
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
        MoveCard(_draggedNote, _noteStartX + delta.X, _noteStartY + delta.Y);
        e.Handled = true;
    }

    internal void MoveCard(NoteCardViewModel note, double x, double y)
    {
        _manualLayout = true;
        var maxX = Math.Max(0, WallScrollViewer.ViewportWidth - note.DisplayWidth);
        note.MoveTo(Math.Clamp(x, 0, maxX), y);
        UpdateSurfaceHeight();
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
