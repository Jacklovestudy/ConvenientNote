using ConvenientNote.Domain;

namespace ConvenientNote.Domain.Notes;

public sealed class Note
{
    public Note(
        NoteId id,
        string title,
        string content,
        NotePosition position,
        NoteSize size,
        string color,
        int zIndex,
        bool isCompleted,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        Title = NormalizeTitle(title);
        Content = content ?? string.Empty;
        Position = position;
        Size = size;
        Color = NormalizeColor(color);
        ZIndex = zIndex;
        IsCompleted = isCompleted;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public NoteId Id { get; }

    public string Title { get; private set; }

    public string Content { get; private set; }

    public NotePosition Position { get; private set; }

    public NoteSize Size { get; private set; }

    public string Color { get; private set; }

    public int ZIndex { get; private set; }

    public bool IsCompleted { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

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

    private void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
