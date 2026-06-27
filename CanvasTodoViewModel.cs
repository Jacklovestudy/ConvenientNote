using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using Prism.Mvvm;

namespace ConvenientNote
{
    public sealed class CanvasTodoViewModel : BindableBase
    {
        private readonly Func<CanvasTodoViewModel, Task> _completionChangedAsync;
        private string _content;
        private string _title;
        private double _x;
        private double _y;
        private bool _isCompleted;

        public CanvasTodoViewModel(
            NoteSnapshot note,
            Func<CanvasTodoViewModel, Task> completionChangedAsync)
        {
            Id = note.Id;
            _title = note.Title;
            _content = note.Content;
            _x = note.X;
            _y = note.Y;
            Width = note.Width;
            Height = note.Height;
            Color = note.Color;
            ZIndex = note.ZIndex;
            _isCompleted = note.IsCompleted;
            _completionChangedAsync = completionChangedAsync;
        }

        public NoteId Id { get; }

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string Content
        {
            get => _content;
            set => SetProperty(ref _content, value);
        }

        public double X
        {
            get => _x;
            private set => SetProperty(ref _x, value);
        }

        public double Y
        {
            get => _y;
            private set => SetProperty(ref _y, value);
        }

        public double Width { get; }

        public double Height { get; }

        public string Color { get; }

        public int ZIndex { get; }

        public bool IsCompleted
        {
            get => _isCompleted;
            set
            {
                if (SetProperty(ref _isCompleted, value))
                {
                    _ = _completionChangedAsync(this);
                }
            }
        }

        public void MoveTo(double x, double y)
        {
            X = Math.Max(0, x);
            Y = Math.Max(0, y);
        }
    }
}
