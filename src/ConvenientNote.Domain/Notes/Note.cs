using ConvenientNote.Domain;

namespace ConvenientNote.Domain.Notes;

public sealed class Note
{
    public const string DefaultBoardKey = "day-todo";
    public const string DefaultPriority = "blue";
    private static readonly HashSet<string> AllowedPriorities = new(StringComparer.OrdinalIgnoreCase)
    {
        "red",
        "green",
        "blue"
    };
    private List<string> _tags;

    public Note(
        NoteId id,
        string boardKey,
        string priority,
        string title,
        string content,
        NotePosition position,
        NoteSize size,
        string color,
        int zIndex,
        bool isCompleted,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string richContent = "",
        NotebookId? notebookId = null,
        IEnumerable<string>? tags = null,
        bool isPinned = false,
        bool isFavorite = false,
        bool isDeleted = false)
    {
        Id = id;
        BoardKey = NormalizeBoardKey(boardKey);
        Priority = NormalizePriority(priority);
        Title = NormalizeTitle(title);
        Content = content ?? string.Empty;
        Position = position;
        Size = size;
        Color = NormalizeColor(color);
        ZIndex = zIndex;
        IsCompleted = isCompleted;
        RichContent = richContent ?? string.Empty;
        NotebookId = notebookId;
        _tags = NormalizeTags(tags ?? []);
        IsPinned = isPinned;
        IsFavorite = isFavorite;
        IsDeleted = isDeleted;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public NoteId Id { get; }

    public string BoardKey { get; }

    public string Priority { get; private set; }

    public string Title { get; private set; }

    public string Content { get; private set; }

    public NotePosition Position { get; private set; }

    public NoteSize Size { get; private set; }

    public string Color { get; private set; }

    public int ZIndex { get; private set; }

    public bool IsCompleted { get; private set; }

    public string RichContent { get; private set; }

    public NotebookId? NotebookId { get; private set; }

    public IReadOnlyList<string> Tags => _tags.AsReadOnly();

    public bool IsPinned { get; private set; }

    public bool IsFavorite { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Note Create(
        string boardKey,
        string title,
        string content,
        NotePosition position,
        NoteSize size,
        string color,
        int zIndex)
    {
        var now = DateTimeOffset.UtcNow;

        return new Note(
            NoteId.New(),
            boardKey,
            DefaultPriority,
            title,
            content,
            position,
            size,
            color,
            zIndex,
            false,
            now,
            now);
    }

    public void Rename(string title)
    {
        Title = NormalizeTitle(title);
        Touch();
    }

    public void UpdateContent(string content)
    {
        Content = content ?? string.Empty;
        Touch();
    }

    public void UpdateRichContent(string richContent, string plainText)
    {
        RichContent = richContent ?? string.Empty;
        Content = plainText ?? string.Empty;
        Touch();
    }

    public void SetNotebook(NotebookId? notebookId)
    {
        NotebookId = notebookId;
        Touch();
    }

    public void SetTags(IEnumerable<string> tags)
    {
        _tags = NormalizeTags(tags ?? []);
        Touch();
    }

    public void SetPinned(bool isPinned)
    {
        IsPinned = isPinned;
        Touch();
    }

    public void SetFavorite(bool isFavorite)
    {
        IsFavorite = isFavorite;
        Touch();
    }

    public void MoveToTrash()
    {
        IsDeleted = true;
        Touch();
    }

    public void Restore()
    {
        IsDeleted = false;
        Touch();
    }

    public void MoveTo(NotePosition position)
    {
        Position = position;
        Touch();
    }

    public void ResizeTo(NoteSize size)
    {
        Size = size;
        Touch();
    }

    public void ChangeColor(string color)
    {
        Color = NormalizeColor(color);
        Touch();
    }

    public void SetZIndex(int zIndex)
    {
        ZIndex = zIndex;
        Touch();
    }

    public void SetCompletion(bool isCompleted)
    {
        IsCompleted = isCompleted;
        Touch();
    }

    public void SetPriority(string priority)
    {
        Priority = NormalizePriority(priority);
        Touch();
    }

    private static string NormalizeTitle(string title)
    {
        var normalized = string.IsNullOrWhiteSpace(title) ? "新便签" : title.Trim();

        if (normalized.Length > 80)
        {
            throw new DomainException("Note title cannot exceed 80 characters.");
        }

        return normalized;
    }

    public static string NormalizeBoardKey(string? boardKey)
    {
        var normalized = string.IsNullOrWhiteSpace(boardKey) ? DefaultBoardKey : boardKey.Trim();

        if (normalized.Length > 64)
        {
            throw new DomainException("Note board key cannot exceed 64 characters.");
        }

        return normalized;
    }

    public static string NormalizePriority(string? priority)
    {
        var normalized = string.IsNullOrWhiteSpace(priority) ? DefaultPriority : priority.Trim().ToLowerInvariant();

        if (!AllowedPriorities.Contains(normalized))
        {
            throw new DomainException("Note priority must be red, green, or blue.");
        }

        return normalized;
    }

    private static string NormalizeColor(string color)
    {
        return string.IsNullOrWhiteSpace(color) ? "#FFF8B8" : color.Trim();
    }

    private static List<string> NormalizeTags(IEnumerable<string> tags)
    {
        var normalized = tags
            .Select(static tag => tag?.Trim() ?? string.Empty)
            .Where(static tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        if (normalized.Any(static tag => tag.Length > 24))
        {
            throw new DomainException("Tag name cannot exceed 24 characters.");
        }

        return normalized;
    }

    private void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
