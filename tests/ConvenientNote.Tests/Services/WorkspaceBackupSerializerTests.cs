using System.IO;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;
using ConvenientNote.Services;
using Xunit;

namespace ConvenientNote.Tests.Services;

public sealed class WorkspaceBackupSerializerTests
{
    [Fact]
    public async Task RoundTripPreservesEveryWorkspaceAndNoteField()
    {
        var workspaceId = new WorkspaceId(Guid.Parse("b6a92bc8-8b88-4c23-a4e6-96299dc8b73c"));
        var noteId = new NoteId(Guid.Parse("f4572f3d-fd1e-454e-80ec-f496bc32a779"));
        var notebookId = new NotebookId(Guid.Parse("9f006cff-6ddb-45bc-9ce6-4ab1e750f0f5"));
        var workspaceCreatedAt = new DateTimeOffset(2026, 8, 1, 9, 30, 0, TimeSpan.FromHours(8));
        var workspaceUpdatedAt = new DateTimeOffset(2026, 8, 30, 21, 45, 0, TimeSpan.FromHours(8));
        var noteCreatedAt = new DateTimeOffset(2026, 8, 2, 10, 15, 0, TimeSpan.FromHours(8));
        var noteUpdatedAt = new DateTimeOffset(2026, 8, 31, 8, 5, 0, TimeSpan.FromHours(8));
        var richContent = "{\"version\":1,\"blocks\":[{\"kind\":\"paragraph\",\"fontSize\":18,\"lineSpacing\":1.5,\"inlines\":[{\"kind\":\"text\",\"text\":\"带格式内容\",\"bold\":true},{\"kind\":\"image\",\"text\":\"f4572f3d-fd1e-454e-80ec-f496bc32a779/photo.png\"}]}]}";
        var snapshot = new WorkspaceSnapshot(
            workspaceId,
            "迁移工作区",
            workspaceCreatedAt,
            workspaceUpdatedAt,
            [new NoteSnapshot(
                noteId,
                "testing",
                "red",
                "完整字段笔记",
                "纯文本正文",
                123.45,
                -67.89,
                456.75,
                234.5,
                "#12ABEF",
                42,
                true,
                richContent,
                notebookId,
                ["工作", "导入导出"],
                true,
                true,
                true,
                noteCreatedAt,
                noteUpdatedAt)]);

        var document = WorkspaceBackupSerializer.CreateDocument(snapshot);
        using var stream = new MemoryStream();
        await WorkspaceBackupSerializer.WriteDocumentAsync(stream, document);
        stream.Position = 0;
        var restored = WorkspaceBackupSerializer.ToWorkspace(
            await WorkspaceBackupSerializer.ReadDocumentAsync(stream));

        Assert.Equal(workspaceId.Value, document.WorkspaceId);
        Assert.Equal("迁移工作区", document.WorkspaceName);
        Assert.Equal(workspaceCreatedAt, document.CreatedAt);
        Assert.Equal(workspaceUpdatedAt, document.UpdatedAt);
        Assert.Equal(workspaceId, restored.Id);
        Assert.Equal("迁移工作区", restored.Name);
        Assert.Equal(workspaceCreatedAt, restored.CreatedAt);
        Assert.Equal(workspaceUpdatedAt, restored.UpdatedAt);

        var note = Assert.Single(restored.Notes);
        Assert.Equal(noteId, note.Id);
        Assert.Equal("testing", note.BoardKey);
        Assert.Equal("red", note.Priority);
        Assert.Equal("完整字段笔记", note.Title);
        Assert.Equal("纯文本正文", note.Content);
        Assert.Equal(123.45, note.Position.X);
        Assert.Equal(-67.89, note.Position.Y);
        Assert.Equal(456.75, note.Size.Width);
        Assert.Equal(234.5, note.Size.Height);
        Assert.Equal("#12ABEF", note.Color);
        Assert.Equal(42, note.ZIndex);
        Assert.True(note.IsCompleted);
        Assert.Equal(richContent, note.RichContent);
        Assert.Equal(notebookId, note.NotebookId);
        Assert.Equal(["工作", "导入导出"], note.Tags);
        Assert.True(note.IsPinned);
        Assert.True(note.IsFavorite);
        Assert.True(note.IsDeleted);
        Assert.Equal(noteCreatedAt, note.CreatedAt);
        Assert.Equal(noteUpdatedAt, note.UpdatedAt);
    }

    [Fact]
    public async Task ReadDocumentAsyncRejectsInvalidDomainValues()
    {
        var document = new WorkspaceBackupDocument(
            Guid.Parse("b6a92bc8-8b88-4c23-a4e6-96299dc8b73c"),
            "迁移工作区",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [new WorkspaceBackupNote(
                Guid.Parse("f4572f3d-fd1e-454e-80ec-f496bc32a779"),
                "testing",
                "purple",
                "无效优先级",
                "正文",
                0,
                0,
                260,
                150,
                "#FFF8B8",
                1,
                false,
                "{}",
                null,
                [],
                false,
                false,
                false,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow)]);
        using var stream = new MemoryStream();
        await WorkspaceBackupSerializer.WriteDocumentAsync(stream, document);
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(
            () => WorkspaceBackupSerializer.ReadDocumentAsync(stream));
    }

    [Fact]
    public async Task WriteDocumentAsyncHonorsAnAlreadyCancelledToken()
    {
        var document = CreateMinimalDocument();
        using var stream = new MemoryStream();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => WorkspaceBackupSerializer.WriteDocumentAsync(stream, document, cancellationSource.Token));
    }

    [Fact]
    public async Task ReadDocumentAsyncHonorsAnAlreadyCancelledToken()
    {
        var document = CreateMinimalDocument();
        using var stream = new MemoryStream();
        await WorkspaceBackupSerializer.WriteDocumentAsync(stream, document);
        stream.Position = 0;
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => WorkspaceBackupSerializer.ReadDocumentAsync(stream, cancellationSource.Token));
    }

    private static WorkspaceBackupDocument CreateMinimalDocument() => new(
        Guid.Parse("6ca0a0cd-1e36-45a1-9110-a51117bc53db"),
        "可取消工作区",
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        []);
}
