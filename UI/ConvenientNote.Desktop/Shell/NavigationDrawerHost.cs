using System.Windows;
using System.Windows.Controls;
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
    private int _templateVersion;
    private FrameworkElement? _stateRoot;
    private DrawerVisualStateManager? _stateManager;

    public NavigationDrawerHost() => Unloaded += (_, _) => RestoreContentCache();

    protected override void OnTemplateChanged(ControlTemplate oldTemplate, ControlTemplate newTemplate)
    {
        // A null template does not reach OnApplyTemplate, but still ends the cache lease.
        DetachTemplate();
        base.OnTemplateChanged(oldTemplate, newTemplate);
    }

    public override void OnApplyTemplate()
    {
        DetachTemplate();
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
        _stateRoot = root;
        _stateManager = new DrawerVisualStateManager();
        VisualStateManager.SetCustomVisualStateManager(root, _stateManager);
        left.CurrentStateChanging += AnimationStarting;
        VisualStateManager.GoToState(this, IsLeftDrawerOpen ? "LeftDrawerOpen" : "LeftDrawerClosed", false);
        VisualStateManager.GoToState(this, IsLeftDrawerOpen || IsRightDrawerOpen || IsTopDrawerOpen || IsBottomDrawerOpen ? "AnyOpen" : "AllClosed", false);
    }

    private void DetachTemplate()
    {
        _templateVersion++;
        RestoreContentCache();
        if (_leftGroup is not null) _leftGroup.CurrentStateChanging -= AnimationStarting;
        if (_panel is not null) _panel.SizeChanged -= DrawerSizeChanged;
        if (_stateRoot is not null && _stateManager is not null)
        {
            _stateManager.Stop(_stateRoot);
            if (ReferenceEquals(VisualStateManager.GetCustomVisualStateManager(_stateRoot), _stateManager))
            {
                VisualStateManager.SetCustomVisualStateManager(_stateRoot, null);
            }
        }
        _leftGroup = null;
        _panel = null;
        _slide = null;
        _stateRoot = null;
        _stateManager = null;
    }

    private Storyboard Slide(bool open, FrameworkElement shadow, Duration duration)
    {
        var storyboard = new Storyboard();
        AddDouble(storyboard, _panel!, new PropertyPath("(0).(1)", RenderTransformProperty, TranslateTransform.XProperty), open ? 0 : null, duration);
        AddDouble(storyboard, shadow, new PropertyPath(OpacityProperty), open ? 1 : 0, duration);
        var templateVersion = _templateVersion;
        storyboard.Completed += (sender, _) =>
        {
            // WPF also delivers completion notifications for clocks stopped by a reversal.
            // Its VisualStateGroup completion bookkeeping can then refer to an old transition.
            // Only the actual finishing slide owns the temporary content-cache lease.
            if (templateVersion == _templateVersion && IsLeftDrawerOpen == open &&
                sender is System.Windows.Media.Animation.Clock { CurrentState: ClockState.Filling })
            {
                RestoreContentCache();
            }
        };
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
        if (!IsLoaded || TransitionAssist.GetDisableTransitions(this) ||
            _cachedContent is not null || Content is not UIElement content) return;
        _cachedContent = content;
        _previousCache = content.CacheMode;
        content.SetCurrentValue(CacheModeProperty, new BitmapCache { EnableClearType = true, SnapsToDevicePixels = true });
    }

    private void RestoreContentCache()
    {
        _cachedContent?.SetCurrentValue(CacheModeProperty, _previousCache);
        _cachedContent = null;
        _previousCache = null;
    }
    /// <summary>
    /// Owns the two render-only transition clocks. Reusing WPF's VisualTransition completion
    /// bookkeeping lets a cancelled transition stop a newer clock for the same destination.
    /// VSM still owns current-state updates and notifications; other groups use its normal path.
    /// </summary>
    private sealed class DrawerVisualStateManager : VisualStateManager
    {
        private readonly Dictionary<VisualStateGroup, Storyboard> _running = new();

        public void Stop(FrameworkElement stateGroupsRoot)
        {
            foreach (var storyboard in _running.Values.Distinct())
            {
                storyboard.Remove(stateGroupsRoot);
            }
            _running.Clear();
        }

        protected override bool GoToStateCore(FrameworkElement control, FrameworkElement stateGroupsRoot,
            string stateName, VisualStateGroup group, VisualState state, bool useTransitions)
        {
            if (group is null || state is null) return false;
            if (group.Name is not ("LeftDrawer" or "AllDrawers"))
            {
                return base.GoToStateCore(control, stateGroupsRoot, stateName, group!, state!, useTransitions);
            }
            if (group.CurrentState == state && _running.ContainsKey(group)) return true;

            var storyboard = useTransitions
                ? group.Transitions.Cast<VisualTransition>()
                    .FirstOrDefault(t => t.From == group.CurrentState?.Name && t.To == state.Name)?.Storyboard
                    ?? state.Storyboard
                : state.Storyboard;
            var stateStoryboard = state.Storyboard;
            try
            {
                // Advance the logical state without allowing VSM to create transition clocks.
                state.Storyboard = null;
                base.GoToStateCore(control, stateGroupsRoot, stateName, group, state, false);
            }
            finally
            {
                state.Storyboard = stateStoryboard;
            }

            if (storyboard is not null)
            {
                // Begin first to snapshot the in-flight value, so reversals do not jump.
                storyboard.Begin(stateGroupsRoot, HandoffBehavior.SnapshotAndReplace, true);
                if (_running.TryGetValue(group, out var previous) && !ReferenceEquals(previous, storyboard))
                {
                    previous.Remove(stateGroupsRoot);
                }
                _running[group] = storyboard;
            }
            return true;
        }
    }

}
