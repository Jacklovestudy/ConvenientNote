using MaterialDesignThemes.Wpf;

namespace ConvenientNote
{
    public enum NavigationSection
    {
        DayTodo,
        Notes,
        Schedule,
        Inbox,
        Review,
        Completed,
        Trash,
        ColorPicker
    }

    public sealed record NavigationItemViewModel(
        NavigationSection Section,
        string ViewName,
        string Title,
        string Description,
        PackIconKind IconKind);

    public sealed record NavigationCatalog(IReadOnlyList<NavigationItemViewModel> Items);
}
