using MaterialDesignThemes.Wpf;

namespace ConvenientNote
{
    public sealed record NavigationItemViewModel(
        string Title,
        string Description,
        PackIconKind IconKind);
}
