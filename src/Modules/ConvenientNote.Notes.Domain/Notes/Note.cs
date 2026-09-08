using ConvenientNote.Notes.Domain;

namespace ConvenientNote.Notes.Domain.Notes;

public sealed class Note
{
    private List<string> _tags;

    public LegacyNoteMetadata LegacyMetadata { get; }

    public Note(
        NoteId id,
        string title,
        string content,
        NotePosition position,
        NoteSize size,
        string color,
        int zIndex,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string richContent = "",
        NotebookId? notebookId = null,
        IEnumerable<string>? tags = null,
        bool isPinned = false,
        bool isFavorite = false,
        bool isDeleted = false,
        LegacyNoteMetadata? legacyMetadata = null)
    {
        Id = id;
        Title = NormalizeTitle(title);
        Content = content ?? string.Empty;
        Position = position;
        Size = size;
        Color = NormalizeColor(color);
        ZIndex = zIndex;
        RichContent = richContent ?? string.Empty;
        NotebookId = notebookId;
        _tags = NormalizeTags(tags ?? []);
        IsPinned = isPinned;
        IsFavorite = isFavorite;
        IsDeleted = isDeleted;
        LegacyMetadata = legacyMetadata ?? new();
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public NoteId Id
    {
        get;
    }

    public string Title
    {
        get; private set;
    }

    public string Content
    {
        get; private set;
    }

    public NotePosition Position
    {
        get; private set;
    }

    public NoteSize Size
    {
        get; private set;
    }

    public string Color
    {
        get; private set;
    }

    public int ZIndex
    {
        get; private set;
    }

    public string RichContent
    {
        get; private set;
    }

    public NotebookId? NotebookId
    {
        get; private set;
    }

    public IReadOnlyList<string> Tags => _tags.AsReadOnly();

    public bool IsPinned
    {
        get; private set;
    }

    public bool IsFavorite
    {
        get; private set;
    }

    public bool IsDeleted
    {
        get; private set;
    }

    public DateTimeOffset CreatedAt
    {
        get;
    }

    public DateTimeOffset UpdatedAt
    {
        get; private set;
    }

    public static Note Create(
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
            title,
            content,
            position,
            size,
            color,
            zIndex,
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

    private static string NormalizeTitle(string title)
    {
        var normalized = string.IsNullOrWhiteSpace(title) ? "新便签" : title.Trim();

        if (normalized.Length > 80)
        {
            throw new DomainException("Note title cannot exceed 80 characters.");
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
