using System.Globalization;
using ConvenientNote.Todos.Application;
using ConvenientNote.Todos.Domain;
using Microsoft.Data.Sqlite;

namespace ConvenientNote.Todos.Infrastructure;
public sealed class SqliteTodoRepository(string databasePath) : ITodoRepository
{
    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = """
    CREATE TABLE IF NOT EXISTS todos_items(
      workspace_id TEXT NOT NULL,id TEXT NOT NULL,title TEXT NOT NULL,content TEXT NOT NULL,priority TEXT NOT NULL,
      x REAL NOT NULL,y REAL NOT NULL,width REAL NOT NULL,height REAL NOT NULL,color TEXT NOT NULL,z_index INTEGER NOT NULL,
      completed INTEGER NOT NULL,deleted INTEGER NOT NULL,created_at TEXT NOT NULL,updated_at TEXT NOT NULL,planned_date TEXT,completed_at TEXT,
      PRIMARY KEY(workspace_id,id));
    """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<TodoItem>> ListAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,title,content,priority,x,y,width,height,color,z_index,completed,deleted,created_at,updated_at,planned_date,completed_at FROM todos_items WHERE workspace_id=$workspace ORDER BY z_index,id";
        command.Parameters.AddWithValue("$workspace", workspaceId.ToString());
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<TodoItem>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(TodoItem.Restore(new(Guid.Parse(reader.GetString(0))), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6), reader.GetDouble(7), reader.GetString(8), reader.GetInt32(9), reader.GetBoolean(10), reader.GetBoolean(11), DateTimeOffset.Parse(reader.GetString(12), CultureInfo.InvariantCulture), DateTimeOffset.Parse(reader.GetString(13), CultureInfo.InvariantCulture), reader.IsDBNull(14) ? null : DateTime.ParseExact(reader.GetString(14), "yyyy-MM-dd", CultureInfo.InvariantCulture), reader.IsDBNull(15) ? null : DateTimeOffset.Parse(reader.GetString(15), CultureInfo.InvariantCulture)));
        return result;
    }

    public Task SaveAsync(Guid workspaceId, IReadOnlyList<TodoItem> items, CancellationToken cancellationToken = default) => WriteAsync(workspaceId, items, false, cancellationToken);
    public Task ImportLegacyAsync(Guid workspaceId, IReadOnlyList<TodoItem> items, CancellationToken cancellationToken = default) => WriteAsync(workspaceId, items, true, cancellationToken);
    private async Task WriteAsync(Guid workspaceId, IReadOnlyList<TodoItem> items, bool insertOnly, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        foreach (var item in items)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
    INSERT INTO todos_items(workspace_id,id,title,content,priority,x,y,width,height,color,z_index,completed,deleted,created_at,updated_at,planned_date,completed_at)
    VALUES($workspace,$id,$title,$content,$priority,$x,$y,$width,$height,$color,$z,$completed,$deleted,$created,$updated,$date,$completedAt)
    """ + (insertOnly ? " ON CONFLICT(workspace_id,id) DO NOTHING" : " ON CONFLICT(workspace_id,id) DO UPDATE SET title=excluded.title,content=excluded.content,priority=excluded.priority,x=excluded.x,y=excluded.y,width=excluded.width,height=excluded.height,color=excluded.color,z_index=excluded.z_index,completed=excluded.completed,deleted=excluded.deleted,updated_at=excluded.updated_at,planned_date=excluded.planned_date,completed_at=excluded.completed_at");
            var values = new Dictionary<string, object?>
            {
                {
                    "workspace",
                    workspaceId.ToString()
                },
                {
                    "id",
                    item.Id.Value.ToString()
                },
                {
                    "title",
                    item.Title
                },
                {
                    "content",
                    item.Content
                },
                {
                    "priority",
                    item.Priority
                },
                {
                    "x",
                    item.X
                },
                {
                    "y",
                    item.Y
                },
                {
                    "width",
                    item.Width
                },
                {
                    "height",
                    item.Height
                },
                {
                    "color",
                    item.Color
                },
                {
                    "z",
                    item.ZIndex
                },
                {
                    "completed",
                    item.IsCompleted
                },
                {
                    "deleted",
                    item.IsDeleted
                },
                {
                    "created",
                    item.CreatedAt.ToString("O")
                },
                {
                    "updated",
                    item.UpdatedAt.ToString("O")
                },
                {
                    "date",
                    item.PlannedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                },
                {
                    "completedAt",
                    item.CompletedAt?.ToString("O")
                }
            };
            foreach (var pair in values)
                command.Parameters.AddWithValue("$" + pair.Key, pair.Value ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
