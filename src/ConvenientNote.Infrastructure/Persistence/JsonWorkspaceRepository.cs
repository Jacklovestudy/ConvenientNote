using System.Text.Json;
using ConvenientNote.Application.Abstractions;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;

namespace ConvenientNote.Infrastructure.Persistence;

public sealed class JsonWorkspaceRepository : IWorkspaceRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public JsonWorkspaceRepository()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConvenientNote",
            "workspaces.json"))
    {
    }

    public JsonWorkspaceRepository(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken = default)
    {
        var records = await LoadRecordsAsync(cancellationToken);

        return records.Select(ToDomain).ToList();
    }

    public async Task<Workspace?> GetAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
        var workspaces = await ListAsync(cancellationToken);

        return workspaces.FirstOrDefault(workspace => workspace.Id == workspaceId);
    }

    public async Task SaveAsync(
        Workspace workspace,
        CancellationToken cancellationToken = default)
    {
        var records = await LoadRecordsAsync(cancellationToken);
        records.RemoveAll(record => record.Id == workspace.Id.Value);
        records.Add(ToRecord(workspace));

        await SaveRecordsAsync(records, cancellationToken);
    }

    public async Task DeleteAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
        var records = await LoadRecordsAsync(cancellationToken);
        records.RemoveAll(record => record.Id == workspaceId.Value);

        await SaveRecordsAsync(records, cancellationToken);
    }

    private async Task<List<WorkspaceRecord>> LoadRecordsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new List<WorkspaceRecord>();
        }

        await using var stream = File.OpenRead(_filePath);

        return await JsonSerializer.DeserializeAsync<List<WorkspaceRecord>>(
                stream,
                JsonOptions,
                cancellationToken)
            ?? new List<WorkspaceRecord>();
    }

    private async Task SaveRecordsAsync(
        List<WorkspaceRecord> records,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, records, JsonOptions, cancellationToken);
    }

    private static Workspace ToDomain(WorkspaceRecord record)
    {
        var notes = record.Notes.Select(note => new Note(
            new NoteId(note.Id),
            note.Title,
            note.Content,
            new NotePosition(note.X, note.Y),
            new NoteSize(note.Width, note.Height),
            note.Color,
            note.ZIndex,
            note.IsCompleted,
            note.CreatedAt,
            note.UpdatedAt));

        return new Workspace(
            new WorkspaceId(record.Id),
            record.Name,
            record.CreatedAt,
            record.UpdatedAt,
            notes);
    }

    private static WorkspaceRecord ToRecord(Workspace workspace)
    {
        return new WorkspaceRecord
        {
            Id = workspace.Id.Value,
            Name = workspace.Name,
            CreatedAt = workspace.CreatedAt,
            UpdatedAt = workspace.UpdatedAt,
            Notes = workspace.Notes.Select(note => new NoteRecord
            {
                Id = note.Id.Value,
                Title = note.Title,
                Content = note.Content,
                X = note.Position.X,
                Y = note.Position.Y,
                Width = note.Size.Width,
                Height = note.Size.Height,
                Color = note.Color,
                ZIndex = note.ZIndex,
                IsCompleted = note.IsCompleted,
                CreatedAt = note.CreatedAt,
                UpdatedAt = note.UpdatedAt
            }).ToList()
        };
    }

    private sealed record WorkspaceRecord
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public List<NoteRecord> Notes { get; init; } = new();
    }

    private sealed record NoteRecord
    {
        public Guid Id { get; init; }

        public string Title { get; init; } = string.Empty;

        public string Content { get; init; } = string.Empty;

        public double X { get; init; }

        public double Y { get; init; }

        public double Width { get; init; }

        public double Height { get; init; }

        public string Color { get; init; } = "#FFF8B8";

        public int ZIndex { get; init; }

        public bool IsCompleted { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }
    }
}
