using System.IO;
using System.Windows.Documents;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Services;
using Xunit;

namespace ConvenientNote.Tests.Services;

public sealed class RichTextAndMediaServiceTests
{
    [Fact]
    public void RichTextRoundTripPreservesParagraphAndBoldText()
    {
        RunSta(() =>
        {
            var document = new FlowDocument();
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run("普通"));
            paragraph.Inlines.Add(new Bold(new Run("加粗")));
            document.Blocks.Add(paragraph);
            var service = new RichTextDocumentService();

            var saved = service.Save(document);
            var loaded = service.Load(saved.Json, string.Empty);

            Assert.Equal("普通加粗", service.ExtractPlainText(loaded));
            Assert.IsType<Bold>(Assert.Single(Assert.Single(loaded.Blocks.OfType<Paragraph>()).Inlines.OfType<Bold>()));
        });
    }

    [Fact]
    public void RichTextRoundTripPreservesInlineFontSizes()
    {
        RunSta(() =>
        {
            var document = new FlowDocument();
            var paragraph = new Paragraph(new Run("小号") { FontSize = 12 });
            paragraph.Inlines.Add(new Run("大号") { FontSize = 24 });
            document.Blocks.Add(paragraph);
            var service = new RichTextDocumentService();

            var saved = service.Save(document);
            var loaded = service.Load(saved.Json, string.Empty);
            var runs = Assert.Single(loaded.Blocks.OfType<Paragraph>()).Inlines.OfType<Run>().ToList();

            Assert.Collection(
                runs,
                run => Assert.Equal(12, run.FontSize),
                run => Assert.Equal(24, run.FontSize));
        });
    }

    [Fact]
    public void CorruptRichTextFallsBackToPlainText()
    {
        RunSta(() =>
        {
            var service = new RichTextDocumentService();
            var document = service.Load("not-json", "可恢复正文");
            Assert.Equal("可恢复正文", service.ExtractPlainText(document));
        });
    }

    [Fact]
    public async Task ImportedMediaUsesNoteScopedRandomName()
    {
        var root = Path.Combine(Path.GetTempPath(), "ConvenientNote.Media.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source.png");
        await File.WriteAllBytesAsync(source, [137, 80, 78, 71]);
        try
        {
            var service = new NoteMediaService(root);
            var noteId = NoteId.New();

            var relative = await service.ImportAsync(noteId, source);

            Assert.StartsWith($"{noteId.Value:N}{Path.DirectorySeparatorChar}", relative);
            Assert.True(File.Exists(Path.Combine(root, relative)));
            Assert.NotEqual("source.png", Path.GetFileName(relative));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void RunSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception current)
            {
                exception = current;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (exception is not null)
        {
            throw exception;
        }
    }
}
