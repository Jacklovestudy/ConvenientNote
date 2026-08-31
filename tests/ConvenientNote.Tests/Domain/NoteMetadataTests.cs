using ConvenientNote.Domain.Notes;
using Xunit;

namespace ConvenientNote.Tests.Domain;

public sealed class NoteMetadataTests
{
    [Fact]
    public void NoteMetadataChangesAreNormalizedAndPersisted()
    {
        var note = Note.Create(
            "testing",
            "想法",
            string.Empty,
            new NotePosition(20, 30),
            new NoteSize(280, 180),
            "#FFF8B8",
            1);
        var notebookId = NotebookId.New();

        note.SetNotebook(notebookId);
        note.SetTags([" 工作 ", "灵感", "工作"]);
        note.SetPinned(true);
        note.SetFavorite(true);
        note.UpdateRichContent("{\"version\":1,\"blocks\":[]}", "正文");

        Assert.Equal(notebookId, note.NotebookId);
        Assert.Equal(["工作", "灵感"], note.Tags);
        Assert.True(note.IsPinned);
        Assert.True(note.IsFavorite);
        Assert.Equal("正文", note.Content);
        Assert.Equal("{\"version\":1,\"blocks\":[]}", note.RichContent);
    }

    [Fact]
    public void NoteCanMoveToTrashAndBeRestored()
    {
        var note = Note.Create(
            "testing",
            "想法",
            string.Empty,
            new NotePosition(20, 30),
            new NoteSize(280, 180),
            "#FFF8B8",
            1);

        note.MoveToTrash();
        Assert.True(note.IsDeleted);

        note.Restore();
        Assert.False(note.IsDeleted);
    }
}
