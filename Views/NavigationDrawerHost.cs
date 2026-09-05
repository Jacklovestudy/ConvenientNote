using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MaterialDesignThemes.Wpf;

namespace ConvenientNote.Views;

public sealed class NavigationDrawerHost : DrawerHost
{
    private static readonly Duration SlideDuration = new(TimeSpan.FromMilliseconds(250));
    private VisualStateGroup? _leftGroup;
    private FrameworkElement? _panel;
    private TranslateTransform? _slide;
    private UIElement? _cachedContent;
    private CacheMode? _previousCache;

    public NavigationDrawerHost() => Unloaded += (_, _) => RestoreContentCache();

    public override void OnApplyTemplate()
    {
        RestoreContentCache();
        if (_leftGroup is not null)
        {
            _leftGroup.CurrentStateChanging -= AnimationStarting;
            _leftGroup.CurrentStateChanged -= AnimationFinished;
        }
        if (_panel is not null) _panel.SizeChanged -= DrawerSizeChanged;
        base.OnApplyTemplate();
        if (OpenMode != DrawerHostOpenMode.Modal || VisualTreeHelper.GetChildrenCount(this) == 0 ||
            VisualTreeHelper.GetChild(this, 0) is not FrameworkElement root ||
            GetTemplateChild("PART_LeftDrawer") is not FrameworkElement panel ||
            GetTemplateChild("PART_ContentCover") is not FrameworkElement cover ||
            GetTemplateChild("LeftDrawerShadow") is not FrameworkElement shadow) return;

        var groups = VisualStateManager.GetVisualStateGroups(root).Cast<VisualStateGroup>().ToList();
        var left = groups.FirstOrDefault(g => g.Name == "LeftDrawer");
        var all = groups.FirstOrDefault(g => g.Name == "AllDrawers");
        if (left is null || all is null) return;
        left.CurrentState?.Storyboard?.Remove(root);
        all.CurrentState?.Storyboard?.Remove(root);

        // The theme slides by animating Margin, which invalidates layout every frame.
        // Keep the modal drawer's layout fixed and move only its rendered pixels.
        _panel = panel;
        panel.BeginAnimation(MarginProperty, null);
        panel.Margin = new Thickness(0);
        _slide = new TranslateTransform(-panel.ActualWidth, 0);
        panel.RenderTransform = _slide;
        panel.SizeChanged += DrawerSizeChanged;
        _leftGroup = left;
        ReplaceStates(left, "LeftDrawerOpen", "LeftDrawerClosed",
            Slide(true, shadow, new Duration(TimeSpan.Zero)), Slide(false, shadow, new Duration(TimeSpan.Zero)),
            Slide(true, shadow, SlideDuration), Slide(false, shadow, SlideDuration));
        ReplaceStates(all, "AnyOpen", "AllClosed",
            Cover(cover, true, new Duration(TimeSpan.Zero)), Cover(cover, false, new Duration(TimeSpan.Zero)),
            Cover(cover, true, SlideDuration), Cover(cover, false, SlideDuration));
        left.CurrentStateChanging += AnimationStarting;
        left.CurrentStateChanged += AnimationFinished;
        VisualStateManager.GoToState(this, IsLeftDrawerOpen ? "LeftDrawerOpen" : "LeftDrawerClosed", false);
        VisualStateManager.GoToState(this, IsLeftDrawerOpen || IsRightDrawerOpen || IsTopDrawerOpen || IsBottomDrawerOpen ? "AnyOpen" : "AllClosed", false);
    }

    private Storyboard Slide(bool open, FrameworkElement shadow, Duration duration)
    {
        var storyboard = new Storyboard();
        AddDouble(storyboard, _panel!, new PropertyPath("(0).(1)", RenderTransformProperty, TranslateTransform.XProperty), open ? 0 : null, duration);
        AddDouble(storyboard, shadow, new PropertyPath(OpacityProperty), open ? 1 : 0, duration);
        return storyboard;
    }

    private static Storyboard Cover(FrameworkElement cover, bool open, Duration duration)
    {
        var storyboard = new Storyboard();
        AddDouble(storyboard, cover, new PropertyPath(OpacityProperty), open ? .56 : 0, duration);
        var hitTesting = new BooleanAnimationUsingKeyFrames();
        // Block clicks while opening/closing, then immediately release the body.
        hitTesting.KeyFrames.Add(new DiscreteBooleanKeyFrame(open, KeyTime.FromTimeSpan(open ? TimeSpan.Zero : duration.TimeSpan)));
        Storyboard.SetTarget(hitTesting, cover);
        Storyboard.SetTargetProperty(hitTesting, new PropertyPath(IsHitTestVisibleProperty));
        storyboard.Children.Add(hitTesting);
        return storyboard;
    }

    private static void AddDouble(Storyboard storyboard, DependencyObject target, PropertyPath property, double? to, Duration duration)
    {
        var animation = new DoubleAnimation { To = to, Duration = duration, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    private static void ReplaceStates(VisualStateGroup group, string openName, string closedName,
        Storyboard open, Storyboard closed, Storyboard opening, Storyboard closing)
    {
        group.Transitions.Clear();
        group.States.Cast<VisualState>().Single(s => s.Name == openName).Storyboard = open;
        group.States.Cast<VisualState>().Single(s => s.Name == closedName).Storyboard = closed;
        group.Transitions.Add(new VisualTransition { From = closedName, To = openName, Storyboard = opening });
        group.Transitions.Add(new VisualTransition { From = openName, To = closedName, Storyboard = closing });
    }

    private void DrawerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_slide is not null) _slide.X = -e.NewSize.Width;
    }

    private void AnimationStarting(object? sender, VisualStateChangedEventArgs e)
    {
        if (_cachedContent is not null || Content is not UIElement content) return;
        _cachedContent = content;
        _previousCache = content.CacheMode;
        content.SetCurrentValue(CacheModeProperty, new BitmapCache { EnableClearType = true, SnapsToDevicePixels = true });
    }

    private void AnimationFinished(object? sender, VisualStateChangedEventArgs e) => RestoreContentCache();

    private void RestoreContentCache()
    {
        _cachedContent?.SetCurrentValue(CacheModeProperty, _previousCache);
        _cachedContent = null;
        _previousCache = null;
    }
}
