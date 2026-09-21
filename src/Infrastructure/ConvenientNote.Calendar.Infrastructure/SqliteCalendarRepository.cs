using System.Globalization;
using System.Text.Json;
using ConvenientNote.Calendar.Application;
using ConvenientNote.Calendar.Domain;
using Microsoft.Data.Sqlite;

namespace ConvenientNote.Calendar.Infrastructure;

public sealed partial class SqliteCalendarRepository(string databasePath) : ICalendarRepository, ICalendarBatchRepository
{
    private static readonly SemaphoreSlim SchemaGate = new(1, 1);
    private sealed record Metadata(string Details = "", Guid? BatchId = null, string BatchName = "", bool IsChecklist = false, bool IsUnscheduled = false);
    private readonly string _path = Path.GetFullPath(databasePath);

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, DefaultTimeout = 30 }.ToString());
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await SchemaGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS Calendar_Events (
                    WorkspaceId TEXT NOT NULL, Id TEXT NOT NULL, Title TEXT NOT NULL,
                    StartsAt TEXT NOT NULL, EndsAt TEXT NOT NULL, IsAllDay INTEGER NOT NULL,
                    IsCompleted INTEGER NOT NULL, PRIMARY KEY (WorkspaceId, Id));
                """;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            command.CommandText = "PRAGMA table_info(Calendar_Events)";
            bool hasMetadata = false;
            using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    if (reader.GetString(1) == "Metadata") hasMetadata = true;
            if (!hasMetadata)
            {
                command.CommandText = "ALTER TABLE Calendar_Events ADD COLUMN Metadata TEXT NOT NULL DEFAULT '{}'";
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS Calendar_ImportBatches (
                    WorkspaceId TEXT NOT NULL, Id TEXT NOT NULL, Fingerprint TEXT NOT NULL, Data TEXT NOT NULL,
                    PRIMARY KEY (WorkspaceId,Id), UNIQUE(WorkspaceId,Fingerprint));
                """;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            // Mark old placeholder dates once; subsequent explicit scheduling is never reclassified.
            command.CommandText = """
                UPDATE Calendar_Events AS e
                SET Metadata=json_set(Metadata,'$.IsUnscheduled',json(CASE WHEN
                    json_extract(Metadata,'$.IsChecklist')=1 AND IsAllDay=1
                    AND json_extract(Metadata,'$.Details') LIKE '截止日期未指定；暂放在行程首日，可在导入预览中修改日期。%'
                    AND json_extract(Metadata,'$.BatchId') IS NOT NULL
                    AND StartsAt=(SELECT MIN(d.StartsAt) FROM Calendar_Events AS d
                        WHERE d.WorkspaceId=e.WorkspaceId
                        AND json_extract(d.Metadata,'$.BatchId')=json_extract(e.Metadata,'$.BatchId')
                        AND COALESCE(json_extract(d.Metadata,'$.IsChecklist'),0)=0)
                    THEN 'true' ELSE 'false' END))
                WHERE json_type(Metadata,'$.IsUnscheduled') IS NULL;
                UPDATE Calendar_Events SET Metadata=json_set(Metadata,'$.Details',
                    replace(json_extract(Metadata,'$.Details'),
                        '截止日期未指定；暂放在行程首日，可在导入预览中修改日期。',
                        '截止日期未指定；保存在待安排，可按需指定日期。'))
                WHERE json_extract(Metadata,'$.IsUnscheduled')=1
                    AND json_extract(Metadata,'$.Details') LIKE '截止日期未指定；暂放在行程首日，可在导入预览中修改日期。%';
                """;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            transaction.Commit();
            }
            finally { SchemaGate.Release(); }
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task<IReadOnlyList<CalendarEvent>> ListAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,Title,StartsAt,EndsAt,IsAllDay,IsCompleted,Metadata FROM Calendar_Events WHERE WorkspaceId=$workspace ORDER BY StartsAt,Id";
        command.Parameters.AddWithValue("$workspace", workspaceId.ToString("D"));
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<CalendarEvent>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var metadata = JsonSerializer.Deserialize<Metadata>(reader.GetString(6)) ?? new();
            result.Add(CalendarEvent.Restore(Guid.Parse(reader.GetString(0)), reader.GetString(1),
                DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), reader.GetBoolean(4), reader.GetBoolean(5),
                metadata.Details, metadata.BatchId, metadata.BatchName, metadata.IsChecklist, metadata.IsUnscheduled));
        }
        return result;
    }

    public async Task SaveAsync(Guid workspaceId, CalendarEvent item, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await SaveOnConnectionAsync(connection, null, workspaceId, item, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SaveOnConnectionAsync(SqliteConnection connection, SqliteTransaction? transaction, Guid workspaceId, CalendarEvent item, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Calendar_Events (WorkspaceId,Id,Title,StartsAt,EndsAt,IsAllDay,IsCompleted,Metadata)
            VALUES($workspace,$id,$title,$start,$end,$allDay,$completed,$metadata)
            ON CONFLICT(WorkspaceId,Id) DO UPDATE SET Title=excluded.Title,StartsAt=excluded.StartsAt,
            EndsAt=excluded.EndsAt,IsAllDay=excluded.IsAllDay,IsCompleted=excluded.IsCompleted,Metadata=excluded.Metadata
            """;
        command.Parameters.AddWithValue("$workspace", workspaceId.ToString("D"));
        command.Parameters.AddWithValue("$id", item.Id.ToString("D"));
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$start", item.Start.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$end", item.End.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$allDay", item.IsAllDay);
        command.Parameters.AddWithValue("$completed", item.IsCompleted);
        command.Parameters.AddWithValue("$metadata", JsonSerializer.Serialize(new Metadata(item.Details, item.BatchId, item.BatchName, item.IsChecklist, item.IsUnscheduled)));
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
