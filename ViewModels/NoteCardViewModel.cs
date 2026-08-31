using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using Prism.Mvvm;

namespace ConvenientNote.ViewModels;

public sealed class NoteCardViewModel : BindableBase
{
    private string _title;
    private string _content;
    private string _richContent;
    private double _x;
    private double _y;
    private bool _isPinned;
    private bool _isFavorite;
    private IReadOnlyList<string> _tags;
    private NotebookId? _notebookId;

    public NoteCardViewModel(NoteSnapshot snapshot)
    {
        Id = snapshot.Id;
        _title = snapshot.Title;
        _content = snapshot.Content;
        _richContent = snapshot.RichContent;
        _x = snapshot.X;
        _y = snapshot.Y;
        Width = Math.Max(280, snapshot.Width);
        Height = Math.Max(180, snapshot.Height);
        Color = snapshot.Color;
        ZIndex = snapshot.ZIndex;
        _isPinned = snapshot.IsPinned;
        _isFavorite = snapshot.IsFavorite;
        _tags = snapshot.Tags;
        _notebookId = snapshot.NotebookId;
        CreatedAt = snapshot.CreatedAt;
        UpdatedAt = snapshot.UpdatedAt;
    }

    public NoteId Id { get; }
    public double Width { get; }
    public double Height { get; }
    public string Color { get; }
    public int ZIndex { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Content
    {
        get => _content;
        set
        {
            if (SetProperty(ref _content, value))
            {
                RaisePropertyChanged(nameof(Preview));
            }
        }
    }

    public string RichContent
    {
        get => _richContent;
        set => SetProperty(ref _richContent, value);
    }

    public string Preview => string.IsNullOrWhiteSpace(Content) ? "双击开始记录…" : Content.Replace('\r', ' ').Replace('\n', ' ').Trim();

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

    public bool IsPinned
    {
        get => _isPinned;
        set => SetProperty(ref _isPinned, value);
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetProperty(ref _isFavorite, value);
    }

    public IReadOnlyList<string> Tags
    {
        get => _tags;
        set
        {
            if (SetProperty(ref _tags, value))
            {
                RaisePropertyChanged(nameof(TagsText));
            }
        }
    }

    public NotebookId? NotebookId
    {
        get => _notebookId;
        set
        {
            if (SetProperty(ref _notebookId, value))
            {
                RaisePropertyChanged(nameof(NotebookName));
            }
        }
    }

    public string NotebookName => NotebookOptions.GetName(NotebookId);

    public string TagsText => Tags.Count == 0 ? "未添加标签" : string.Join("  ·  ", Tags);

    public string UpdatedText => UpdatedAt.LocalDateTime.ToString("MM月dd日 HH:mm");

    public void MoveTo(double x, double y)
    {
        X = Math.Max(0, x);
        Y = Math.Max(0, y);
    }
}

public sealed record NotebookOption(NotebookId? Id, string Name);

public static class NotebookOptions
{
    public static readonly NotebookId AllId = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    public static readonly NotebookId WorkId = new(Guid.Parse("94ad5bf4-7253-4f7c-a005-4538d4366df2"));
    public static readonly NotebookId StudyId = new(Guid.Parse("2e5caa6a-a77b-4bf1-9c46-1091378dc4c6"));
    public static readonly NotebookId LifeId = new(Guid.Parse("292a6c23-fadf-4e2f-944a-a8d46736251f"));

    public static IReadOnlyList<NotebookOption> All { get; } =
    [
        new(AllId, "全部笔记本"),
        new(null, "未分类"),
        new(WorkId, "工作"),
        new(StudyId, "学习"),
        new(LifeId, "生活")
    ];

    public static IReadOnlyList<NotebookOption> Editable { get; } = All.Where(option => option.Id != AllId).ToList();

    public static string GetName(NotebookId? id) => Editable.FirstOrDefault(option => option.Id == id)?.Name ?? "未分类";
}
