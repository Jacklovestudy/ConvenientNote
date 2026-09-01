using System.IO;

namespace ConvenientNote.Services;

public sealed class UnsupportedWorkspaceBackupSchemaException(int schemaVersion)
    : Exception($"Backup schema version {schemaVersion} is newer than this application supports.")
{
    public int SchemaVersion { get; } = schemaVersion;
}

internal static class WorkspaceBackupImportFailureMessages
{
    internal const string NewerSchemaMessage = "备份版本较新，请升级应用后重试";
    internal const string GenericMessage = "导入失败，请检查备份文件后重试";

    public static string GetMessage(Exception exception) =>
        exception is UnsupportedWorkspaceBackupSchemaException
            ? NewerSchemaMessage
            : GenericMessage;
}
