using System.Globalization;
using ConvenientNote.Calendar.Application;
using ConvenientNote.Calendar.Domain;
using Microsoft.Data.Sqlite;

namespace ConvenientNote.Calendar.Infrastructure;

public sealed class SqliteCalendarRepository(string databasePath) : ICalendarRepository
{
    private readonly string _path = Path.GetFullPath(databasePath);

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, DefaultTimeout = 30 }.ToString());
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS Calendar_Events (
                    WorkspaceId TEXT NOT NULL, Id TEXT NOT NULL, Title TEXT NOT NULL,
                    StartsAt TEXT NOT NULL, EndsAt TEXT NOT NULL, IsAllDay INTEGER NOT NULL,
                    IsCompleted INTEGER NOT NULL, PRIMARY KEY (WorkspaceId, Id));
                """;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task<IReadOnlyList<CalendarEvent>> ListAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,Title,StartsAt,EndsAt,IsAllDay,IsCompleted FROM Calendar_Events WHERE WorkspaceId=$workspace ORDER BY StartsAt,Id";
        command.Parameters.AddWithValue("$workspace", workspaceId.ToString("D"));
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<CalendarEvent>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(CalendarEvent.Restore(Guid.Parse(reader.GetString(0)), reader.GetString(1),
                DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), reader.GetBoolean(4), reader.GetBoolean(5)));
        return result;
    }

    public async Task SaveAsync(Guid workspaceId, CalendarEvent item, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Calendar_Events VALUES($workspace,$id,$title,$start,$end,$allDay,$completed)
            ON CONFLICT(WorkspaceId,Id) DO UPDATE SET Title=excluded.Title,StartsAt=excluded.StartsAt,
            EndsAt=excluded.EndsAt,IsAllDay=excluded.IsAllDay,IsCompleted=excluded.IsCompleted
            """;
        command.Parameters.AddWithValue("$workspace", workspaceId.ToString("D"));
        command.Parameters.AddWithValue("$id", item.Id.ToString("D"));
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$start", item.Start.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$end", item.End.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$allDay", item.IsAllDay);
        command.Parameters.AddWithValue("$completed", item.IsCompleted);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid workspaceId, Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Calendar_Events WHERE WorkspaceId=$workspace AND Id=$id";
        command.Parameters.AddWithValue("$workspace", workspaceId.ToString("D"));
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
