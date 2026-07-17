using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConvenientNote.ViewModels;

namespace ConvenientNote.Views
{
    public partial class TodoBoardControl : UserControl
    {
        private const double AlignmentSnapDistance = 10;
        private const double DraggedTodoScale = 1.035;

        private CanvasTodoViewModel? _draggedTodo;
        private FrameworkElement? _draggedElement;
        private Point _dragStartMousePosition;
        private Point _dragStartTodoPosition;
        private bool _isVerticalGuideVisible;
        private bool _isHorizontalGuideVisible;

        public TodoBoardControl()
        {
            InitializeComponent();
            TodoBoardScrollViewer.AddHandler(
                MouseWheelEvent,
                new MouseWheelEventHandler(TodoBoardScrollViewer_MouseWheel),
                true);
        }

        private void TodoBoardScrollViewer_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                TodoBoardScrollViewer.ScrollToHorizontalOffset(TodoBoardScrollViewer.HorizontalOffset - e.Delta);
            }
            else
            {
                TodoBoardScrollViewer.ScrollToVerticalOffset(TodoBoardScrollViewer.VerticalOffset - e.Delta);
            }

            e.Handled = true;
        }

        private async void ArrangeTodosButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not TodoBoardViewModel viewModel)
            {
                return;
            }

            var arranged = await viewModel.ArrangeTodosAsync(
                TodoBoardScrollViewer.ViewportWidth);

            if (arranged)
            {
                TodoBoardScrollViewer.ScrollToHorizontalOffset(0);
                TodoBoardScrollViewer.ScrollToVerticalOffset(0);
            }
        }

        private void TodoCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInteractiveElement(e.OriginalSource as DependencyObject))
            {
                return;
            }

            if (sender is not FrameworkElement element ||
                element.DataContext is not CanvasTodoViewModel todo)
            {
                return;
            }

            var canvas = FindVisualAncestor<Canvas>(element);
            if (canvas is null)
            {
                return;
            }

            _draggedTodo = todo;
            _draggedElement = element;
            _dragStartMousePosition = e.GetPosition(canvas);
            _dragStartTodoPosition = new Point(todo.X, todo.Y);

            element.CaptureMouse();
            AnimateTodoScale(element, DraggedTodoScale);
            e.Handled = true;
        }

        private void TodoCard_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_draggedTodo is null ||
                _draggedElement is null ||
                e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var canvas = FindVisualAncestor<Canvas>(_draggedElement);
            if (canvas is null)
            {
                return;
            }

            var currentPosition = e.GetPosition(canvas);
            var offset = currentPosition - _dragStartMousePosition;
            var boardSize = new Size(canvas.ActualWidth, canvas.ActualHeight);
            var nextPosition = ApplyAlignmentSnap(
                _draggedTodo,
                _dragStartTodoPosition.X + offset.X,
                _dragStartTodoPosition.Y + offset.Y,
                boardSize,
                out var verticalGuideX,
                out var horizontalGuideY);

            _draggedTodo.MoveTo(nextPosition.X, nextPosition.Y);
            UpdateAlignmentGuides(verticalGuideX, horizontalGuideY, boardSize);

            e.Handled = true;
        }

        private async void TodoCard_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggedTodo is null ||
                _draggedElement is null)
            {
                return;
            }

            _draggedElement.ReleaseMouseCapture();
            AnimateTodoScale(_draggedElement, 1);
            HideAlignmentGuides();

            if (DataContext is TodoBoardViewModel viewModel)
            {
                await viewModel.CommitTodoPositionAsync(_draggedTodo);
            }

            _draggedTodo = null;
            _draggedElement = null;
            e.Handled = true;
        }

        private async void TodoTitle_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: CanvasTodoViewModel todo } &&
                DataContext is TodoBoardViewModel viewModel)
            {
                await viewModel.CommitTodoTitleAsync(todo);
            }
        }

        private async void TodoContent_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: CanvasTodoViewModel todo } &&
                DataContext is TodoBoardViewModel viewModel)
            {
                await viewModel.CommitTodoContentAsync(todo);
            }
        }

        private async void PriorityButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: CanvasTodoViewModel todo } &&
                DataContext is TodoBoardViewModel viewModel)
            {
                todo.CyclePriority();
                await viewModel.CommitTodoPriorityAsync(todo);
            }

            e.Handled = true;
        }

        private Point ApplyAlignmentSnap(
            CanvasTodoViewModel draggedTodo,
            double x,
            double y,
            Size boardSize,
            out double? verticalGuideX,
            out double? horizontalGuideY)
        {
            verticalGuideX = null;
            horizontalGuideY = null;

            if (DataContext is not TodoBoardViewModel viewModel)
            {
                return new Point(x, y);
            }

            var verticalSnap = FindSnap(
                x,
                draggedTodo.Width,
                viewModel.TodoItems
                    .Where(todo => !ReferenceEquals(todo, draggedTodo))
                    .SelectMany(todo => new[]
                    {
                        todo.X,
                        todo.X + todo.Width / 2,
                        todo.X + todo.Width
                    }));

            if (verticalSnap is not null)
            {
                x += verticalSnap.Value.Offset;
                verticalGuideX = verticalSnap.Value.GuidePosition;
            }

            var horizontalSnap = FindSnap(
                y,
                draggedTodo.Height,
                viewModel.TodoItems
                    .Where(todo => !ReferenceEquals(todo, draggedTodo))
                    .SelectMany(todo => new[]
                    {
                        todo.Y,
                        todo.Y + todo.Height / 2,
                        todo.Y + todo.Height
                    }));

            if (horizontalSnap is not null)
            {
                y += horizontalSnap.Value.Offset;
                horizontalGuideY = horizontalSnap.Value.GuidePosition;
            }

            x = Math.Clamp(x, 0, Math.Max(0, boardSize.Width - draggedTodo.Width));
            y = Math.Clamp(y, 0, Math.Max(0, boardSize.Height - draggedTodo.Height));

            return new Point(x, y);
        }

        private static SnapMatch? FindSnap(
            double position,
            double length,
            IEnumerable<double> candidatePositions)
        {
            var offsets = new[] { 0, length / 2, length };
            SnapMatch? bestMatch = null;
            var bestDistance = AlignmentSnapDistance;

            foreach (var candidatePosition in candidatePositions)
            {
                foreach (var offset in offsets)
                {
                    var delta = candidatePosition - (position + offset);
                    var distance = Math.Abs(delta);

                    if (distance <= bestDistance)
                    {
                        bestDistance = distance;
                        bestMatch = new SnapMatch(delta, candidatePosition);
                    }
                }
            }

            return bestMatch;
        }

        private void UpdateAlignmentGuides(
            double? verticalGuideX,
            double? horizontalGuideY,
            Size boardSize)
        {
            AlignmentGuideLayer.Width = boardSize.Width;
            AlignmentGuideLayer.Height = boardSize.Height;

            if (verticalGuideX is { } x)
            {
                var crispX = Math.Round(x) + 0.5;
                VerticalAlignmentGuide.X1 = crispX;
                VerticalAlignmentGuide.X2 = crispX;
                VerticalAlignmentGuide.Y1 = 0;
                VerticalAlignmentGuide.Y2 = boardSize.Height;
                SetGuideVisibility(VerticalAlignmentGuide, true, ref _isVerticalGuideVisible);
            }
            else
            {
                SetGuideVisibility(VerticalAlignmentGuide, false, ref _isVerticalGuideVisible);
            }

            if (horizontalGuideY is { } y)
            {
                var crispY = Math.Round(y) + 0.5;
                HorizontalAlignmentGuide.X1 = 0;
                HorizontalAlignmentGuide.X2 = boardSize.Width;
                HorizontalAlignmentGuide.Y1 = crispY;
                HorizontalAlignmentGuide.Y2 = crispY;
                SetGuideVisibility(HorizontalAlignmentGuide, true, ref _isHorizontalGuideVisible);
            }
            else
            {
                SetGuideVisibility(HorizontalAlignmentGuide, false, ref _isHorizontalGuideVisible);
            }
        }

        private void HideAlignmentGuides()
        {
            SetGuideVisibility(VerticalAlignmentGuide, false, ref _isVerticalGuideVisible);
            SetGuideVisibility(HorizontalAlignmentGuide, false, ref _isHorizontalGuideVisible);
        }

        private static void SetGuideVisibility(
            UIElement guide,
            bool isVisible,
            ref bool currentVisibility)
        {
            if (currentVisibility == isVisible)
            {
                return;
            }

            currentVisibility = isVisible;
            guide.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation
                {
                    To = isVisible ? 1 : 0,
                    Duration = TimeSpan.FromMilliseconds(isVisible ? 80 : 140),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }

        private static void AnimateTodoScale(FrameworkElement element, double scale)
        {
            var scaleTransform = element.RenderTransform as ScaleTransform;

            if (scaleTransform is null)
            {
                scaleTransform = new ScaleTransform(1, 1);
                element.RenderTransform = scaleTransform;
            }
            else if (scaleTransform.IsFrozen)
            {
                scaleTransform = scaleTransform.CloneCurrentValue();
                element.RenderTransform = scaleTransform;
            }

            element.RenderTransformOrigin = new Point(0.5, 0.5);

            scaleTransform.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                CreateScaleAnimation(scale));
            scaleTransform.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                CreateScaleAnimation(scale));
        }

        private static DoubleAnimation CreateScaleAnimation(double scale)
        {
            return new DoubleAnimation
            {
                To = scale,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new BackEase
                {
                    Amplitude = 0.25,
                    EasingMode = EasingMode.EaseOut
                }
            };
        }

        private static bool IsInteractiveElement(DependencyObject? source)
        {
            while (source is not null)
            {
                if (source is TextBoxBase or ToggleButton or Button)
                {
                    return true;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        private static T? FindVisualAncestor<T>(DependencyObject source)
            where T : DependencyObject
        {
            var current = source;
            while (current is not null)
            {
                if (current is T target)
                {
                    return target;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private readonly record struct SnapMatch(double Offset, double GuidePosition);
    }
}
