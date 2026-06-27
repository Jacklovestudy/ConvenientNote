using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ConvenientNote
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private CanvasTodoViewModel? _draggedTodo;
        private FrameworkElement? _draggedElement;
        private Point _dragStartMousePosition;
        private Point _dragStartTodoPosition;

        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            MaximizeRestoreText.Text = WindowState == WindowState.Maximized ? "❐" : "□";
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
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

            _draggedTodo.MoveTo(
                _dragStartTodoPosition.X + offset.X,
                _dragStartTodoPosition.Y + offset.Y);

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

            if (DataContext is MainWindowViewModel viewModel)
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
                DataContext is MainWindowViewModel viewModel)
            {
                await viewModel.CommitTodoTitleAsync(todo);
            }
        }

        private async void TodoContent_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: CanvasTodoViewModel todo } &&
                DataContext is MainWindowViewModel viewModel)
            {
                await viewModel.CommitTodoContentAsync(todo);
            }
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
    }
}
