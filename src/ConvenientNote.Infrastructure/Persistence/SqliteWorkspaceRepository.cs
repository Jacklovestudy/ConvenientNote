using ConvenientNote.Application.Abstractions;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;
using ConvenientNote.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ConvenientNote.Infrastructure.Persistence;

public sealed class SqliteWorkspaceRepository : IWorkspaceRepository
{
    private readonly string _databasePath;
    private bool _databaseInitialized;

    public SqliteWorkspaceRepository()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConvenientNote",
            "ConvenientNote.db"))
    {
    }

    public SqliteWorkspaceRepository(string databasePath)
    {
        _databasePath = databasePath;
    }

    public async Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await CreateInitializedContextAsync(cancellationToken);
        var entities = await context.Workspaces
            .AsNoTracking()
            .Include(workspace => workspace.Notes)
            .ToListAsync(cancellationToken);

        return entities
            .OrderBy(workspace => workspace.CreatedAt)
            .Select(ToDomain)
            .ToList();
    }

    public async Task<Workspace?> GetAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await CreateInitializedContextAsync(cancellationToken);
        var entity = await context.Workspaces
            .AsNoTracking()
            .Include(workspace => workspace.Notes)
            .FirstOrDefaultAsync(
                workspace => workspace.Id == workspaceId.Value,
                cancellationToken);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task SaveAsync(
        Workspace workspace,
        CancellationToken cancellationToken = default)
    {
        await using var context = await CreateInitializedContextAsync(cancellationToken);
        var entity = await context.Workspaces
            .Include(current => current.Notes)
            .FirstOrDefaultAsync(
                current => current.Id == workspace.Id.Value,
                cancellationToken);

        if (entity is null)
        {
            context.Workspaces.Add(ToEntity(workspace));
        }
        else
        {
            entity.Name = workspace.Name;
            entity.CreatedAt = workspace.CreatedAt;
            entity.UpdatedAt = workspace.UpdatedAt;

            var noteIds = workspace.Notes
                .Select(note => note.Id.Value)
                .ToHashSet();
            var removedNotes = entity.Notes
                .Where(note => !noteIds.Contains(note.Id))
                .ToList();

            foreach (var removedNote in removedNotes)
            {
                context.Notes.Remove(removedNote);
            }

            var existingNotes = entity.Notes.ToDictionary(note => note.Id);
            foreach (var note in workspace.Notes)
            {
                if (existingNotes.TryGetValue(note.Id.Value, out var noteEntity))
                {
                    UpdateEntity(noteEntity, note, workspace.Id);
                }
                else
                {
                    context.Notes.Add(ToEntity(note, workspace.Id));
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await CreateInitializedContextAsync(cancellationToken);
        var entity = await context.Workspaces
            .FirstOrDefaultAsync(
                workspace => workspace.Id == workspaceId.Value,
                cancellationToken);

        if (entity is null)
        {
            return;
        }

        context.Workspaces.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<ConvenientNoteDbContext> CreateInitializedContextAsync(
        CancellationToken cancellationToken)
    {
        var context = CreateContext();
        await InitializeDatabaseAsync(context, cancellationToken);

        return context;
    }

    private ConvenientNoteDbContext CreateContext()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new DbContextOptionsBuilder<ConvenientNoteDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;

        return new ConvenientNoteDbContext(options);
    }

    private async Task InitializeDatabaseAsync(
        ConvenientNoteDbContext context,
        CancellationToken cancellationToken)
    {
        if (_databaseInitialized)
        {
            return;
        }

        await context.Database.EnsureCreatedAsync(cancellationToken);
        await TryImportFromJsonAsync(context, cancellationToken);
        _databaseInitialized = true;
    }

    private static async Task TryImportFromJsonAsync(
        ConvenientNoteDbContext context,
        CancellationToken cancellationToken)
    {
        if (await context.Workspaces.AnyAsync(cancellationToken))
        {
            return;
        }

        var jsonPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConvenientNote",
            "workspaces.json");

        if (!File.Exists(jsonPath))
        {
            return;
        }

        var jsonRepository = new JsonWorkspaceRepository(jsonPath);
        var workspaces = await jsonRepository.ListAsync(cancellationToken);

        if (workspaces.Count == 0)
        {
            return;
        }

        context.Workspaces.AddRange(workspaces.Select(ToEntity));
        await context.SaveChangesAsync(cancellationToken);
    }

    private static Workspace ToDomain(WorkspaceEntity entity)
    {
        var notes = entity.Notes.Select(note => new Note(
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
            new WorkspaceId(entity.Id),
            entity.Name,
            entity.CreatedAt,
            entity.UpdatedAt,
            notes);
    }

    private static WorkspaceEntity ToEntity(Workspace workspace)
    {
        return new WorkspaceEntity
        {
            Id = workspace.Id.Value,
            Name = workspace.Name,
            CreatedAt = workspace.CreatedAt,
            UpdatedAt = workspace.UpdatedAt,
            Notes = workspace.Notes
                .Select(note => ToEntity(note, workspace.Id))
                .ToList()
        };
    }

    private static NoteEntity ToEntity(Note note, WorkspaceId workspaceId)
    {
        var entity = new NoteEntity
        {
            Id = note.Id.Value,
        };

        UpdateEntity(entity, note, workspaceId);

        return entity;
    }

    private static void UpdateEntity(
        NoteEntity entity,
        Note note,
        WorkspaceId workspaceId)
    {
        entity.WorkspaceId = workspaceId.Value;
        entity.Title = note.Title;
        entity.Content = note.Content;
        entity.X = note.Position.X;
        entity.Y = note.Position.Y;
        entity.Width = note.Size.Width;
        entity.Height = note.Size.Height;
        entity.Color = note.Color;
        entity.ZIndex = note.ZIndex;
        entity.IsCompleted = note.IsCompleted;
        entity.CreatedAt = note.CreatedAt;
        entity.UpdatedAt = note.UpdatedAt;
    }
}
