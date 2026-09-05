using System.IO;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Infrastructure.Persistence;
using ConvenientNote.Services;
using ConvenientNote.ViewModels;
using Xunit;

namespace ConvenientNote.Tests.Services;

public sealed class KnowledgeMemoPersistenceTests
{
    [Fact]
    public async Task SidebarIsIndependentPersistsDeletionAndRoundTripsWithNoteBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ConvenientNote-KnowledgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "notes.db");
        var repository = new SqliteWorkspaceRepository(path);
        var service = new WorkspaceApplicationService(repository);
        var workspace = await service.GetOrCreateDefaultWorkspaceAsync();
        await service.CreateNoteAsync(workspace.Id, 20, 30, "普通笔记", TodoBoardKeys.Notes);
        var model = new NotesViewModel(service, new RichTextDocumentService(), new NoteMediaService(Path.Combine(directory, "media")));
        await model.InitializeAsync();
        Assert.Equal(149, KnowledgeChecklist.Parse(model.KnowledgeMemoText).Count(r => r.HasCheck));
        Assert.Single(model.FilteredNotes);
        const string text = "### 我的分类　已掌握 1/2\n1. 数组　☑\n2. 内存泄漏　☐";
        Assert.True(await model.SaveKnowledgeMemoAsync(text));
        var saved = await service.GetWorkspaceAsync(workspace.Id);
        Assert.Equal(2, saved.Notes.Count(n => n.BoardKey == TodoBoardKeys.Notes));
        var memo = Assert.Single(saved.Notes, KnowledgeMemoMetadata.IsMemo);
        Assert.Equal(text, memo.Content);
        var backup = NotesBackupSerializer.CreateDocument(saved.Notes);
        var reopened = new NotesViewModel(new WorkspaceApplicationService(new SqliteWorkspaceRepository(path)),
            new RichTextDocumentService(), new NoteMediaService(Path.Combine(directory, "media")));
        await reopened.InitializeAsync();
        Assert.Equal(text, reopened.KnowledgeMemoText);
        Assert.Single(reopened.FilteredNotes);
        Assert.True(await reopened.SaveKnowledgeMemoAsync(""));
        var deleted = await service.GetWorkspaceAsync(workspace.Id);
        Assert.Empty(Assert.Single(deleted.Notes, KnowledgeMemoMetadata.IsMemo).Content);
        var blankModel = new NotesViewModel(service, new RichTextDocumentService(), new NoteMediaService(Path.Combine(directory, "media")));
        await blankModel.InitializeAsync();
        Assert.Empty(blankModel.KnowledgeMemoText);
        Assert.True(blankModel.KnowledgeMemoPersisted);
        await service.ReplaceActiveNotesAsync(workspace.Id, NotesBackupSerializer.ToNotes(backup).ToArray());
        Assert.Equal(text, Assert.Single((await service.GetWorkspaceAsync(workspace.Id)).Notes, KnowledgeMemoMetadata.IsMemo).Content);
        Assert.Equal(memo.Id, Assert.Single((await service.GetWorkspaceAsync(workspace.Id)).Notes, KnowledgeMemoMetadata.IsMemo).Id);
    }
}
