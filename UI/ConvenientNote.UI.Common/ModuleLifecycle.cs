using System.Windows;

namespace ConvenientNote.UI.Common;

public static class ApplicationRegions
{
    public const string Main = "MainRegion";
}

/// <summary>A page's save/veto boundary, independent of its business module.</summary>
public interface IPageLifecycle
{
    bool RequiresNavigationPreparation { get; }
    Task<bool> PrepareToLeaveAsync();
    Task<bool> FlushAsync();
}

/// <summary>The host owns the window; a module supplies its compact content.</summary>
public interface ICompactModeProvider
{
    event EventHandler? Requested;
    FrameworkElement CreateContent();
}
