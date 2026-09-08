using ConvenientNote.Notes.Application;
using ConvenientNote.Notes.Domain.Notes;
using ConvenientNote.Notes.Infrastructure;
using ConvenientNote.Platform.Contracts;
using ConvenientNote.Services;
using Microsoft.Data.Sqlite;
using Xunit;
namespace ConvenientNote.Notes.Tests;
public sealed class BackupTests
{
    [Fact]
    public async Task VersionOneArchiveRoundTripsRichContentMemoAndMediaWithoutTouchingTrash()
    {
        var root = Path.Combine(Path.GetTempPath(), "NotesModuleTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var workspace = new Context();
            var repository = new SqliteNotesRepository(Path.Combine(root, "notes.db"));
            var service = new NotesApplicationService(repository, workspace);
            var note = Note.Create("正文", "plain", new(12, 30), new(320, 240), "#12ABCD", 5);
            note.UpdateRichContent("{\"version\":1,\"blocks\":[]}", "plain");
            note.SetTags(["tag"]);
            var deleted = Note.Create("trash", "keep", new(0, 0), new(260, 150), "#FFFFFF", 6);
            deleted.MoveToTrash();
            await repository.ImportLegacyAsync(workspace.Id, [note, deleted]);
            await service.SaveKnowledgeMemoAsync(new(workspace.Id), "### 清单\n内容 ☑");
            var media = new NoteMediaService(Path.Combine(root, "media"));
            var source = Path.Combine(root, "image.png");
            await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);
            var mediaPath = await media.ImportAsync(note.Id, source);
            var backup = new NotesBackupService(service, media);
            var archive = Path.Combine(root, "backup.cnote");
            Assert.Equal(2, (await backup.ExportAsync(archive)).NoteCount);
            note.Rename("changed");
            await repository.SaveAsync(workspace.Id, note);
            await File.WriteAllBytesAsync(media.GetAbsolutePath(mediaPath), [9]);
            Assert.Equal(2, (await backup.ImportOverwriteAsync(archive)).NoteCount);
            var restored = await repository.ListAsync(workspace.Id);
            Assert.Equal(3, restored.Count);
            Assert.Equal("正文", restored.Single(n => n.Id == note.Id).Title);
            Assert.Equal(note.RichContent, restored.Single(n => n.Id == note.Id).RichContent);
            Assert.True(restored.Single(n => n.Id == deleted.Id).IsDeleted);
            Assert.Contains(restored, n => n.Tags.Contains(KnowledgeMemoMetadata.Tag) && n.Content.Contains("☑"));
            Assert.Equal([1, 2, 3, 4], await File.ReadAllBytesAsync(media.GetAbsolutePath(mediaPath)));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }
    private sealed class Context : IWorkspaceContext
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Task<WorkspaceInfo> GetCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceInfo(Id, "测试"));
    }
}
