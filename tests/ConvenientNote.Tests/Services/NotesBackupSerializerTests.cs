using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Services;
using Xunit;

namespace ConvenientNote.Tests.Services;

public sealed class NotesBackupSerializerTests
{
    private const string CompleteDocumentJson = """
        {
          "notes": [
            {
              "id": "0a100001-0000-0000-0000-000000000001",
              "boardKey": "testing",
              "priority": "blue",
              "title": "标题",
              "content": "正文",
              "x": 12.5,
              "y": -3.25,
              "width": 260,
              "height": 150,
              "color": "#FFF8B8",
              "zIndex": 7,
              "isCompleted": false,
              "richContent": "{}",
              "notebookId": null,
              "tags": ["work"],
              "isPinned": false,
              "isFavorite": false,
              "isDeleted": false,
              "createdAt": "2026-08-01T01:00:00+00:00",
              "updatedAt": "2026-08-02T01:00:00+00:00"
            }
          ]
        }
        """;

    public static IEnumerable<object[]> RequiredDocumentAndNoteProperties()
    {
        yield return ["document", "notes"];
        foreach (var propertyName in new[]
                 {
                     "id", "boardKey", "priority", "title", "content", "x", "y", "width", "height",
                     "color", "zIndex", "isCompleted", "richContent", "notebookId", "tags", "isPinned",
                     "isFavorite", "isDeleted", "createdAt", "updatedAt"
                 })
        {
            yield return ["note", propertyName];
        }
    }

    [Fact]
    public async Task CreateDocumentExportsExactlyFiveActiveNotesAndRoundTripsEveryRichNoteField()
    {
        // A missing Notes-board filter, deleted-note filter, or DTO field must make this fail.
        var createdAt = new DateTimeOffset(2026, 8, 2, 10, 15, 0, TimeSpan.FromHours(8));
        var updatedAt = new DateTimeOffset(2026, 8, 31, 8, 5, 0, TimeSpan.FromHours(8));
        var richContent = "{\"version\":1,\"blocks\":[{\"kind\":\"paragraph\",\"fontSize\":18,\"lineSpacing\":1.5,\"inlines\":[{\"kind\":\"text\",\"text\":\"带格式内容\",\"bold\":true},{\"kind\":\"image\",\"text\":\"0a100001-0000-0000-0000-000000000001/photo.png\"}]}]}";
        var richNoteId = Guid.Parse("0a100001-0000-0000-0000-000000000001");
        var notebookId = Guid.Parse("0a200001-0000-0000-0000-000000000001");
        var snapshots = new[]
        {
            new NoteSnapshot(new NoteId(richNoteId), TodoBoardKeys.Notes, "red", "完整字段笔记", "纯文本正文", 123.45, -67.89, 456.75, 234.5, "#12ABEF", 42, true, richContent, new NotebookId(notebookId), ["工作", "导入导出"], true, true, false, createdAt, updatedAt),
            new NoteSnapshot(new NoteId(Guid.Parse("0a100002-0000-0000-0000-000000000002")), TodoBoardKeys.Notes, "blue", "笔记二", "正文二", 0, 0, 260, 150, "#FFF8B8", 2, false, "{}", null, [], false, false, false, createdAt, updatedAt),
            new NoteSnapshot(new NoteId(Guid.Parse("0a100003-0000-0000-0000-000000000003")), TodoBoardKeys.Notes, "green", "笔记三", "正文三", 1, 2, 260, 150, "#FFF8B8", 3, false, "{}", null, [], false, false, false, createdAt, updatedAt),
            new NoteSnapshot(new NoteId(Guid.Parse("0a100004-0000-0000-0000-000000000004")), TodoBoardKeys.Notes, "blue", "笔记四", "正文四", 3, 4, 260, 150, "#FFF8B8", 4, false, "{}", null, [], false, false, false, createdAt, updatedAt),
            new NoteSnapshot(new NoteId(Guid.Parse("0a100005-0000-0000-0000-000000000005")), TodoBoardKeys.Notes, "red", "笔记五", "正文五", 5, 6, 260, 150, "#FFF8B8", 5, false, "{}", null, [], false, false, false, createdAt, updatedAt),
            new NoteSnapshot(new NoteId(Guid.Parse("0a100006-0000-0000-0000-000000000006")), TodoBoardKeys.Notes, "blue", "已删除笔记", "回收站", 0, 0, 260, 150, "#FFF8B8", 6, false, "{}", null, [], false, false, true, createdAt, updatedAt),
            new NoteSnapshot(new NoteId(Guid.Parse("0a100007-0000-0000-0000-000000000007")), TodoBoardKeys.DayTodo, "blue", "待办一", "待办", 0, 0, 260, 150, "#FFF8B8", 7, false, "{}", null, [], false, false, false, createdAt, updatedAt),
            new NoteSnapshot(new NoteId(Guid.Parse("0a100008-0000-0000-0000-000000000008")), TodoBoardKeys.DayTodo, "blue", "待办二", "待办", 0, 0, 260, 150, "#FFF8B8", 8, false, "{}", null, [], false, false, false, createdAt, updatedAt)
        };

        var document = NotesBackupSerializer.CreateDocument(snapshots);
        using var stream = new MemoryStream();
        await NotesBackupSerializer.WriteDocumentAsync(stream, document);
        stream.Position = 0;
        var restored = NotesBackupSerializer.ToNotes(await NotesBackupSerializer.ReadDocumentAsync(stream));

        Assert.Equal(5, document.Notes.Count);
        Assert.All(document.Notes, static note =>
        {
            Assert.Equal(TodoBoardKeys.Notes, note.BoardKey);
            Assert.False(note.IsDeleted);
        });
        Assert.Equal(5, restored.Count);

        var richNote = Assert.Single(restored, note => note.Id.Value == richNoteId);
        Assert.Equal(TodoBoardKeys.Notes, richNote.BoardKey);
        Assert.Equal("red", richNote.Priority);
        Assert.Equal("完整字段笔记", richNote.Title);
        Assert.Equal("纯文本正文", richNote.Content);
        Assert.Equal(123.45, richNote.Position.X);
        Assert.Equal(-67.89, richNote.Position.Y);
        Assert.Equal(456.75, richNote.Size.Width);
        Assert.Equal(234.5, richNote.Size.Height);
        Assert.Equal("#12ABEF", richNote.Color);
        Assert.Equal(42, richNote.ZIndex);
        Assert.True(richNote.IsCompleted);
        Assert.Equal(richContent, richNote.RichContent);
        Assert.Equal(new NotebookId(notebookId), richNote.NotebookId);
        Assert.Equal(["工作", "导入导出"], richNote.Tags);
        Assert.True(richNote.IsPinned);
        Assert.True(richNote.IsFavorite);
        Assert.False(richNote.IsDeleted);
        Assert.Equal(createdAt, richNote.CreatedAt);
        Assert.Equal(updatedAt, richNote.UpdatedAt);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"notes\":null}")]
    [InlineData("{\"notes\":[null]}")]
    [InlineData("{\"notes\":[{\"id\":\"00000000-0000-0000-0000-000000000000\"}]}")]
    [InlineData("{\"notes\":[{\"id\":\"0a100001-0000-0000-0000-000000000001\"},{\"id\":\"0a100001-0000-0000-0000-000000000001\"}]}")]
    [InlineData("{\"notes\":[{\"id\":\"0a100001-0000-0000-0000-000000000001\",\"boardKey\":\"testing\",\"priority\":null,\"title\":\"标题\",\"content\":\"正文\",\"x\":0,\"y\":0,\"width\":260,\"height\":150,\"color\":\"#FFF8B8\",\"richContent\":\"{}\",\"tags\":[]}]}")]
    [InlineData("{\"notes\":[{\"id\":\"0a100001-0000-0000-0000-000000000001\",\"boardKey\":\"testing\",\"priority\":\"blue\",\"title\":\"标题\",\"content\":\"正文\",\"x\":0,\"y\":0,\"width\":260,\"height\":150,\"color\":\"#FFF8B8\",\"richContent\":\"{}\",\"tags\":null}]}")]
    [InlineData("{\"notes\":[{\"id\":\"0a100001-0000-0000-0000-000000000001\",\"boardKey\":\"day-todo\",\"priority\":\"blue\",\"title\":\"标题\",\"content\":\"正文\",\"x\":0,\"y\":0,\"width\":260,\"height\":150,\"color\":\"#FFF8B8\",\"richContent\":\"{}\",\"tags\":[]}]}")]
    [InlineData("{\"notes\":[{\"id\":\"0a100001-0000-0000-0000-000000000001\",\"boardKey\":\"testing\",\"priority\":\"blue\",\"title\":\"标题\",\"content\":\"正文\",\"x\":0,\"y\":0,\"width\":260,\"height\":150,\"color\":\"#FFF8B8\",\"richContent\":\"{}\",\"tags\":[],\"isDeleted\":true}]}")]
    [InlineData("{\"notes\":[{\"id\":\"0a100001-0000-0000-0000-000000000001\",\"boardKey\":\"testing\",\"priority\":\"purple\",\"title\":\"标题\",\"content\":\"正文\",\"x\":0,\"y\":0,\"width\":260,\"height\":150,\"color\":\"#FFF8B8\",\"richContent\":\"{}\",\"tags\":[]}]}")]
    public async Task ReadDocumentAsyncRejectsMalformedOrNonNotesDocuments(string json)
    {
        // Removing validation or domain reconstruction must make this accept an unsafe import payload.
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => NotesBackupSerializer.ReadDocumentAsync(stream));
    }

    [Theory]
    [MemberData(nameof(RequiredDocumentAndNoteProperties))]
    public async Task ReadDocumentAsyncRejectsEveryOmittedDocumentAndNoteField(
        string targetName,
        string propertyName)
    {
        // Disabling required-constructor-parameter handling must make at least the valid-default omissions pass.
        var root = JsonNode.Parse(CompleteDocumentJson)!.AsObject();
        var target = targetName == "document"
            ? root
            : root["notes"]![0]!.AsObject();
        Assert.True(target.Remove(propertyName));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(root.ToJsonString()));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => NotesBackupSerializer.ReadDocumentAsync(stream));
    }

    [Fact]
    public async Task WriteDocumentAsyncHonorsAnAlreadyCancelledToken()
    {
        using var stream = new MemoryStream();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => NotesBackupSerializer.WriteDocumentAsync(stream, new NotesBackupDocument([]), cancellationSource.Token));
    }

    [Fact]
    public async Task ReadDocumentAsyncHonorsAnAlreadyCancelledToken()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"notes\":[]}"));
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => NotesBackupSerializer.ReadDocumentAsync(stream, cancellationSource.Token));
    }
}
