namespace ConvenientNote.Services;
internal static class NotesBackupImportFailureMessages
{
    internal const string NewerSchemaMessage = "备份版本较新，请升级应用后重试";
    internal const string GenericMessage = "导入失败，请检查备份文件后重试";

    public static string GetMessage(Exception exception) =>
        exception is UnsupportedNotesBackupSchemaException
            ? NewerSchemaMessage
            : GenericMessage;
}
