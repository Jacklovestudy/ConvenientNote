using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using ConvenientNote.Services;
using ConvenientNote.Views;
using Xunit;

namespace ConvenientNote.Tests.Views;

public sealed class NoteOutlinePerformanceTests
{
    [Fact]
    public void UnrelatedNavigationLayoutDoesNotRepositionEveryHeading() => Sta(() =>
    {
        var control = new RichNoteEditorControl();
        var sibling = new Border { Width = 10, Height = 20, HorizontalAlignment = HorizontalAlignment.Left };
        var host = new Grid();
        host.Children.Add(control);
        host.Children.Add(sibling);
        var window = new Window { Content = host, Width = 1400, Height = 820, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            var editor = (RichTextBox)control.FindName("Editor");
            var document = new FlowDocument();
            for (var i = 0; i < 100; i++)
            {
                var heading = new Paragraph(new Run($"Chapter {i}"));
                DocumentOutline.SetHeadingLevel(heading, 1);
                document.Blocks.Add(heading);
                document.Blocks.Add(new Paragraph(new Run(new string('x', 300))));
            }
            editor.Document = document;
            Drain(control);
            var gutter = (Canvas)control.FindName("HeadingGutter");
            var buttons = (List<Button>)typeof(RichNoteEditorControl).GetField("_headingButtons", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
            // Observe the real positioning loop at its Canvas output boundary.
            var probes = buttons.Select(b =>
            {
                var probe = new PositionProbe { Tag = b.Tag, Width = b.Width, Height = b.Height, Visibility = b.Visibility };
                Canvas.SetTop(probe, Canvas.GetTop(b));
                return probe;
            }).ToList();
            gutter.Children.Clear();
            buttons.Clear();
            foreach (var probe in probes) { buttons.Add(probe); gutter.Children.Add(probe); }
            Drain(control);
            foreach (var probe in probes) probe.PositionWrites = 0;

            for (var frame = 0; frame < 30; frame++)
            {
                sibling.Width = 20 + frame;
                host.UpdateLayout();
            }

            Assert.True(probes.Sum(p => p.PositionWrites) == 0,
                $"Unrelated navigation layout wrote heading positions {probes.Sum(p => p.PositionWrites)} times for 100 headings over 30 frames.");
        }
        finally { window.Close(); }
    });

    [Fact]
    public void ScrollResizeAndTextReflowKeepHeadingArrowsAligned() => Sta(() =>
    {
        var control = new RichNoteEditorControl();
        var window = new Window { Content = control, Width = 1400, Height = 820, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            var editor = (RichTextBox)control.FindName("Editor");
            var prefix = new Paragraph(new Run(new string('x', 350)));
            var heading = new Paragraph(new Run("Moving chapter"));
            DocumentOutline.SetHeadingLevel(heading, 1);
            editor.Document = new FlowDocument(prefix);
            editor.Document.Blocks.Add(heading);
            for (var i = 0; i < 50; i++) editor.Document.Blocks.Add(new Paragraph(new Run($"Body {i}")));
            Drain(control);
            var originalTop = AssertAligned();
            editor.ScrollToVerticalOffset(80);
            Drain(control);
            var scrolledTop = AssertAligned();
            Assert.NotEqual(originalTop, scrolledTop);
            window.Width = 1000;
            Drain(control);
            var resizedTop = AssertAligned();
            Assert.NotEqual(scrolledTop, resizedTop);
            prefix.Inlines.Add(new Run(new string('x', 300)));
            Drain(control);
            Assert.NotEqual(resizedTop, AssertAligned());

            double AssertAligned()
            {
                var button = Assert.Single(((Canvas)control.FindName("HeadingGutter")).Children.OfType<Button>());
                var top = heading.ContentStart.GetCharacterRect(LogicalDirection.Forward).Top;
                Assert.InRange(Math.Abs(Canvas.GetTop(button) - top), 0, 1);
                return top;
            }
        }
        finally { window.Close(); }
    });

    private sealed class PositionProbe : Button
    {
        internal int PositionWrites;
        static PositionProbe() => Canvas.TopProperty.OverrideMetadata(typeof(PositionProbe),
            new FrameworkPropertyMetadata(double.NaN, null, (d, value) => { ((PositionProbe)d).PositionWrites++; return value; }));
    }

    private static void Drain(FrameworkElement control)
    {
        control.UpdateLayout();
        control.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        control.UpdateLayout();
    }

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
