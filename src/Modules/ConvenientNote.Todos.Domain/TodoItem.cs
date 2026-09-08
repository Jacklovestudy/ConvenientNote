namespace ConvenientNote.Todos.Domain;
public readonly record struct TodoId(Guid Value)
{
    public static TodoId New() => new(Guid.NewGuid());
}

public sealed class TodoItem
{
    public TodoId Id { get; private init; }
    public string Title { get; private set; } = "";
    public string Content { get; private set; } = "";
    public string Priority { get; private set; } = "blue";
    public double X { get; private set; }
    public double Y { get; private set; }
    public double Width { get; private init; }
    public double Height { get; private init; }
    public string Color { get; private init; } = "";
    public int ZIndex { get; private init; }
    public bool IsCompleted { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTime? PlannedDate { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private TodoItem()
    {
    }

    public static TodoItem Create(string title, double x, double y, int zIndex = 0)
    {
        var now = DateTimeOffset.UtcNow;
        return Restore(TodoId.New(), title, "", "blue", x, y, 260, 150, "#FFF8B8", zIndex, false, false, now, now, null, null);
    }

    public static TodoItem Restore(TodoId id, string title, string content, string priority, double x, double y, double width, double height, string color, int zIndex, bool isCompleted, bool isDeleted, DateTimeOffset createdAt, DateTimeOffset updatedAt, DateTime? plannedDate, DateTimeOffset? completedAt)
    {
        if (id.Value == Guid.Empty)
            throw new ArgumentException("A todo must have an identity.", nameof(id));
        ValidatePosition(x, y);
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        return new TodoItem
        {
            Id = id,
            Title = NormalizeTitle(title),
            Content = content ?? "",
            Priority = NormalizePriority(priority),
            X = Math.Max(0, x),
            Y = Math.Max(0, y),
            Width = width,
            Height = height,
            Color = color,
            ZIndex = zIndex,
            IsCompleted = isCompleted,
            IsDeleted = isDeleted,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            PlannedDate = plannedDate?.Date,
            CompletedAt = isCompleted ? completedAt : null
        };
    }

    public void Rename(string title)
    {
        Title = NormalizeTitle(title);
        Touch();
    }

    public void UpdateContent(string content)
    {
        Content = content ?? "";
        Touch();
    }

    public void SetPriority(string priority)
    {
        Priority = NormalizePriority(priority);
        Touch();
    }

    public void MoveTo(double x, double y)
    {
        ValidatePosition(x, y);
        X = Math.Max(0, x);
        Y = Math.Max(0, y);
        Touch();
    }

    public void SetCompletion(bool completed)
    {
        if (IsCompleted == completed)
            return;
        IsCompleted = completed;
        CompletedAt = completed ? DateTimeOffset.UtcNow : null;
        Touch();
    }

    public void Reschedule(DateTime? date)
    {
        PlannedDate = date?.Date;
        Touch();
    }

    public void Delete()
    {
        IsDeleted = true;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
    private static string NormalizeTitle(string title) => string.IsNullOrWhiteSpace(title) ? "新待办" : title.Trim();
    private static string NormalizePriority(string priority) => priority?.ToLowerInvariant()is "red" or "green" or "blue" ? priority.ToLowerInvariant() : throw new ArgumentOutOfRangeException(nameof(priority));
    private static void ValidatePosition(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
            throw new ArgumentOutOfRangeException(nameof(x));
    }
}
