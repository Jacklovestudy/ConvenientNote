using ConvenientNote.Domain.Notes;

namespace ConvenientNote.Application.Workspaces;

/// <summary>A reserved note tag keeps the sidebar memo in the existing transactional note/backup format.</summary>
public static class KnowledgeMemoMetadata
{
    public const string Tag = "__app_knowledge_memo";
    public static bool IsMemo(NoteSnapshot note) => note.BoardKey == TodoBoardKeys.Notes &&
        note.Tags.Contains(Tag, StringComparer.OrdinalIgnoreCase);
}
