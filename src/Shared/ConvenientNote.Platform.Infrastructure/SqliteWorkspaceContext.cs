using ConvenientNote.Platform.Contracts;
using Microsoft.Data.Sqlite;

namespace ConvenientNote.Platform.Infrastructure;

public sealed class SqliteWorkspaceContext(string databasePath) : IWorkspaceContext
{
    private readonly string _path = Path.GetFullPath(databasePath);

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path }.ToString());
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS Platform_Workspaces(Id TEXT PRIMARY KEY, Name TEXT NOT NULL, SortOrder INTEGER NOT NULL);
                CREATE TABLE IF NOT EXISTS Platform_Migrations(Id TEXT PRIMARY KEY, CompletedAt TEXT NOT NULL);
                """;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task<WorkspaceInfo> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,Name FROM Platform_Workspaces ORDER BY SortOrder,Id LIMIT 1";
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("工作区尚未初始化。");
        return new WorkspaceInfo(Guid.Parse(reader.GetString(0)), reader.GetString(1));
    }

    public async Task ImportWorkspacesAsync(IReadOnlyList<WorkspaceInfo> workspaces, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        for (var index = 0; index < workspaces.Count; index++)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO Platform_Workspaces VALUES($id,$name,$order) ON CONFLICT(Id) DO NOTHING";
            command.Parameters.AddWithValue("$id", workspaces[index].Id.ToString("D"));
            command.Parameters.AddWithValue("$name", workspaces[index].Name);
            command.Parameters.AddWithValue("$order", index);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        transaction.Commit();
    }

    public async Task<bool> HasMigrationAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Platform_Migrations WHERE Id=$id";
        command.Parameters.AddWithValue("$id", id);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0;
    }

    public async Task CompleteMigrationAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Platform_Migrations VALUES($id,$at) ON CONFLICT(Id) DO NOTHING";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
