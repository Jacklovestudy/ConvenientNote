using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConvenientNote.Notes.Domain.Notes;
using ConvenientNote.Services;
using ConvenientNote.Views;
using Xunit;

namespace ConvenientNote.Tests.Views;

public sealed class ClipboardImageImportTests
{
    [Fact]
    public async Task Encoded_clipboard_image_can_be_imported_and_temp_file_is_removed()
    {
        var root = Path.Combine(Path.GetTempPath(), "ConvenientNote-clipboard-test-" + Guid.NewGuid().ToString("N"));
        var media = new NoteMediaService(root);
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 30, 20, 10, 255 }, 4);
        bitmap.Freeze();
        string? temporary = null;
        string? imported = null;
        try
        {
            await RichNoteEditorControl.ImportClipboardBitmapAsync(bitmap, async path =>
            {
                temporary = path;
                imported = await media.ImportAsync(NoteId.New(), path);
            });
            Assert.NotNull(imported);
            Assert.True(File.Exists(media.GetAbsolutePath(imported)));
            Assert.False(File.Exists(temporary));
            var bytes = await File.ReadAllBytesAsync(media.GetAbsolutePath(imported));
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes.Take(8));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
