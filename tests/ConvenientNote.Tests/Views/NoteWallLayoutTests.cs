using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ConvenientNote.Notes.Application;
using ConvenientNote.Notes.Domain.Notes;
using ConvenientNote.ViewModels;
using ConvenientNote.Views;
using Xunit;

namespace ConvenientNote.Tests.Views;

public sealed class NoteWallLayoutTests
{
    [Fact]
    public void DragCreatingAndRemovingOverflowKeepsDroppedPosition() => Sta(() =>
    {
        var note = CreateNote(2);
        var wall = new NoteWallControl { DataContext = new { FilteredNotes = new[] { note } } };
        var window = new Window { Content = wall, Width = 650, Height = 500, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            Pump();
            var scroll = Descendants<ScrollViewer>(wall).First();
            Assert.Equal(0, scroll.ScrollableHeight);
            wall.MoveCard(note, 30, 800);
            Pump();
            Assert.Equal(800, note.DisplayY);
            Assert.True(scroll.ScrollableHeight > 0);
            wall.MoveCard(note, 30, 40);
            Pump();
            Assert.Equal(40, note.DisplayY);
            Assert.Equal(0, scroll.ScrollableHeight);
            window.Width = 430;
            Pump();
            Assert.Equal(16, note.DisplayY);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void CardsFitViewportAfterResizeAndCollectionChangesWithoutOverwritingSavedPositions() => Sta(() =>
    {
        var notes = new ObservableCollection<NoteCardViewModel>(Enumerable.Range(0, 10).Select(CreateNote));
        var wall = new NoteWallControl { DataContext = new { FilteredNotes = notes } };
        var window = new Window { Content = wall, Width = 1000, Height = 500, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            Pump();
            AssertFits(wall, notes.Count);
            window.Width = 430;
            Pump();
            AssertFits(wall, notes.Count);
            notes.Add(CreateNote(10));
            Pump();
            AssertFits(wall, notes.Count);
            Assert.All(notes, note => { Assert.Equal(1500, note.X); Assert.Equal(900, note.Y); });
            notes[0].MoveTo(50, 3000);
            wall.AutoArrange();
            Pump();
            AssertFits(wall, notes.Count);
            Assert.Equal(50, notes[0].X);
            Assert.Equal(3000, notes[0].Y);
            window.Width = 1100;
            window.Height = 650;
            Pump();
            AssertFits(wall, notes.Count);
            notes.Clear();
            Pump();
            Assert.Equal(0, Descendants<ScrollViewer>(wall).First().ScrollableHeight);
        }
        finally { window.Close(); }
    });

    private static void AssertFits(NoteWallControl wall, int count)
    {
        var scroll = Descendants<ScrollViewer>(wall).First();
        var items = Descendants<ItemsControl>(wall).First();
        var bounds = Enumerable.Range(0, count).Select(i =>
        {
            var presenter = (ContentPresenter)items.ItemContainerGenerator.ContainerFromIndex(i);
            var card = Descendants<Border>(presenter).First();
            return card.TransformToAncestor(items).TransformBounds(new Rect(card.RenderSize));
        }).ToArray();
        Assert.All(bounds, rect => { Assert.True(rect.Left >= 0); Assert.True(rect.Right <= scroll.ViewportWidth + 1); });
        for (var i = 0; i < bounds.Length; i++)
            for (var j = i + 1; j < bounds.Length; j++)
                Assert.False(bounds[i].IntersectsWith(bounds[j]));
        Assert.True(scroll.ExtentHeight >= bounds.Max(rect => rect.Bottom));
        Assert.True(scroll.ScrollableHeight > 0);
        Assert.Equal(0, scroll.ScrollableWidth);
    }

    private static NoteCardViewModel CreateNote(int i) => new(new NoteSnapshot(
        new NoteId(Guid.NewGuid()), "notes", "", $"便签 {i}", "正文", 1500, 900, i == 0 ? 800 : 280,
        i == 1 ? 250 : 180, "#FFF9B1", 0, false, "", null, [], false, false, false,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
