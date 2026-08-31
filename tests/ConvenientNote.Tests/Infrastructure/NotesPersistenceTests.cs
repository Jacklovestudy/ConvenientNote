using System.IO;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;
using ConvenientNote.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ConvenientNote.Tests.Infrastructure;

public sealed class NotesPersistenceTests
{
    [Fact]
    public async Task SqliteRoundTripPreservesRichNoteMetadata()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ConvenientNote.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var repository = new SqliteWorkspaceRepository(Path.Combine(directory, "notes.db"));
            var workspace = Workspace.Create("测试");
            var note = workspace.AddNote(
                TodoBoardKeys.Testing,
                "笔记",
                "纯文本",
                new NotePosition(1, 2),
                new NoteSize(300, 200),
                "#FFF8B8");
            note.UpdateRichContent("{\"version\":1}", "富文本摘要");
            note.SetTags(["工作", "灵感"]);
            note.SetPinned(true);
            note.SetFavorite(true);

            await repository.SaveAsync(workspace);
            var loaded = Assert.Single(await repository.ListAsync(), current => current.Id == workspace.Id);
            var loadedNote = Assert.Single(loaded.Notes);

            Assert.Equal("{\"version\":1}", loadedNote.RichContent);
            Assert.Equal(["工作", "灵感"], loadedNote.Tags);
            Assert.True(loadedNote.IsPinned);
            Assert.True(loadedNote.IsFavorite);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
