using System.Text.Json;
using ConvenientNote.Calendar.Application;
using ConvenientNote.Calendar.Domain;
using Microsoft.Data.Sqlite;

namespace ConvenientNote.Calendar.Infrastructure;

public sealed partial class SqliteCalendarRepository
{
    public async Task ImportAsync(Guid workspaceId, CalendarImportBatch batch, IReadOnlyList<CalendarEvent> items, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO Calendar_ImportBatches(WorkspaceId,Id,Fingerprint,Data) VALUES($workspace,$id,$fingerprint,$data)";
        command.Parameters.AddWithValue("$workspace", workspaceId.ToString("D"));
        command.Parameters.AddWithValue("$id", batch.Id.ToString("D"));
        command.Parameters.AddWithValue("$fingerprint", batch.Fingerprint);
        command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(batch));
        try { await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        { throw new InvalidOperationException("这份原文已经导入。请在导入记录中查看；如需重导，可先撤销原批次。", ex); }
        foreach (var item in items)
            await SaveOnConnectionAsync(connection, transaction, workspaceId, item, cancellationToken).ConfigureAwait(false);
        transaction.Commit();
    }

    public async Task<IReadOnlyList<CalendarImportBatch>> ListBatchesAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Data FROM Calendar_ImportBatches WHERE WorkspaceId=$workspace";
        command.Parameters.AddWithValue("$workspace", workspaceId.ToString("D"));
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<CalendarImportBatch>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(JsonSerializer.Deserialize<CalendarImportBatch>(reader.GetString(0))!);
        return result.OrderByDescending(b => b.ImportedAt).ToArray();
    }

    public async Task UndoImportAsync(Guid workspaceId, Guid batchId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM Calendar_Events WHERE WorkspaceId=$workspace AND json_extract(Metadata,'$.BatchId')=$id;
            DELETE FROM Calendar_ImportBatches WHERE WorkspaceId=$workspace AND Id=$id;
            """;
        command.Parameters.AddWithValue("$workspace", workspaceId.ToString("D"));
        command.Parameters.AddWithValue("$id", batchId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        transaction.Commit();
    }
}
