using MaterialDesignThemes.Wpf;

namespace ConvenientNote
{
    public enum NavigationSection
    {
        DayTodo,
        Recent,
        Schedule,
        Inbox,
        Review,
        Completed,
        Trash
    }

    public sealed record NavigationItemViewModel(
        NavigationSection Section,
        string ViewName,
        string Title,
        string Description,
        PackIconKind IconKind);
}
