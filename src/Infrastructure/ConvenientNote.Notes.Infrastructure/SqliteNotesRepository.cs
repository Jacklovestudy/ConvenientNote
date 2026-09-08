using System.Text.Json;
using ConvenientNote.Notes.Application;
using ConvenientNote.Notes.Domain.Notes;
using Microsoft.Data.Sqlite;

namespace ConvenientNote.Notes.Infrastructure;
/// <summary>Owns only notes_records. Writes never replace an entire workspace.</summary>
public sealed class SqliteNotesRepository(string databasePath) : INotesRepository
{

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var path = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        try
        {
            await connection.OpenAsync(ct);
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS notes_records(workspace_id TEXT NOT NULL,id TEXT NOT NULL,payload TEXT NOT NULL,is_deleted INTEGER NOT NULL,PRIMARY KEY(workspace_id,id));";
            await command.ExecuteNonQueryAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<Note>> ListAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM notes_records WHERE workspace_id=$workspace";
        command.Parameters.AddWithValue("$workspace", workspaceId.ToString());
        var notes = new List<Note>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var dto = JsonSerializer.Deserialize<StoredNote>(reader.GetString(0)) ?? throw new InvalidDataException("Invalid stored note.");
            notes.Add(dto.ToNote());
        }
        return notes;
    }

    public Task SaveAsync(Guid workspaceId, Note note, CancellationToken cancellationToken = default) => SaveManyAsync(workspaceId, [note], cancellationToken);

    public Task SaveManyAsync(Guid workspaceId, IReadOnlyCollection<Note> notes, CancellationToken cancellationToken = default) => WriteAsync(workspaceId, notes, false, false, cancellationToken);

    public Task ImportLegacyAsync(Guid workspaceId, IReadOnlyCollection<Note> notes, CancellationToken cancellationToken = default) => WriteAsync(workspaceId, notes, true, false, cancellationToken);

    public Task ReplaceActiveAsync(Guid workspaceId, IReadOnlyCollection<Note> notes, CancellationToken cancellationToken = default) => WriteAsync(workspaceId, notes, false, true, cancellationToken);

    private async Task WriteAsync(Guid workspaceId, IReadOnlyCollection<Note> notes, bool insertOnly, bool replace, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (notes.Select(n => n.Id).Distinct().Count() != notes.Count)
            throw new ArgumentException("Duplicate note IDs.");
        if (replace && notes.Any(n => n.IsDeleted))
            throw new ArgumentException("Replacement must contain active notes only.");
        await using var connection = await OpenAsync(ct);
        using var transaction = connection.BeginTransaction();
        if (replace)
        {
            using var check = connection.CreateCommand();
            check.Transaction = transaction;
            check.CommandText = "SELECT id FROM notes_records WHERE workspace_id=$workspace AND is_deleted=1";
            check.Parameters.AddWithValue("$workspace", workspaceId.ToString());
            var preserved = new HashSet<Guid>();
            using (var reader = await check.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                    preserved.Add(Guid.Parse(reader.GetString(0)));
            }
            if (notes.Any(n => preserved.Contains(n.Id.Value)))
                throw new InvalidOperationException("Imported note collides with preserved trash.");
            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM notes_records WHERE workspace_id=$workspace AND is_deleted=0";
            delete.Parameters.AddWithValue("$workspace", workspaceId.ToString());
            await delete.ExecuteNonQueryAsync(ct);
        }
        foreach (var note in notes)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO notes_records(workspace_id,id,payload,is_deleted) VALUES($workspace,$id,$payload,$deleted) ON CONFLICT(workspace_id,id) " + (insertOnly ? "DO NOTHING" : "DO UPDATE SET payload=excluded.payload,is_deleted=excluded.is_deleted");
            command.Parameters.AddWithValue("$workspace", workspaceId.ToString());
            command.Parameters.AddWithValue("$id", note.Id.Value.ToString());
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(StoredNote.From(note)));
            command.Parameters.AddWithValue("$deleted", note.IsDeleted ? 1 : 0);
            await command.ExecuteNonQueryAsync(ct);
        }
        ct.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    public async Task DeleteAsync(Guid workspaceId, NoteId noteId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM notes_records WHERE workspace_id=$workspace AND id=$id";
        command.Parameters.AddWithValue("$workspace", workspaceId.ToString());
        command.Parameters.AddWithValue("$id", noteId.Value.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record StoredNote(Guid Id, string Title, string Content, double X, double Y, double Width, double Height, string Color, int ZIndex, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string RichContent, Guid? NotebookId, List<string> Tags, bool IsPinned, bool IsFavorite, bool IsDeleted, LegacyNoteMetadata? LegacyMetadata = null)
    {
        public static StoredNote From(Note n) => new(n.Id.Value, n.Title, n.Content, n.Position.X, n.Position.Y, n.Size.Width, n.Size.Height, n.Color, n.ZIndex, n.CreatedAt, n.UpdatedAt, n.RichContent, n.NotebookId?.Value, n.Tags.ToList(), n.IsPinned, n.IsFavorite, n.IsDeleted, n.LegacyMetadata);
        public Note ToNote() => new(new(Id), Title, Content, new(X, Y), new(Width, Height), Color, ZIndex, CreatedAt, UpdatedAt, RichContent, NotebookId.HasValue ? new NotebookId(NotebookId.Value) : null, Tags, IsPinned, IsFavorite, IsDeleted, LegacyMetadata);
    }
}
