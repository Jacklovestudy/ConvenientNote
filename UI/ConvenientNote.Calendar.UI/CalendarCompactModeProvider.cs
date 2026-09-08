using System.Windows;
using ConvenientNote.UI.Common;
using ConvenientNote.ViewModels;
using ConvenientNote.Views;

namespace ConvenientNote.Calendar.UI;

public sealed class CalendarCompactModeProvider(ScheduleViewModel calendar) : ICompactModeProvider
{
    public event EventHandler? Requested
    {
        add => calendar.DesktopModeRequested += value;
        remove => calendar.DesktopModeRequested -= value;
    }

    public FrameworkElement CreateContent() => new CalendarPanel { IsCompact = true, DataContext = calendar };
}
