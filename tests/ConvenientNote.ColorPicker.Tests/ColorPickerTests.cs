using ConvenientNote.ColorPicker.Domain;
using ConvenientNote.ColorPicker.Application;
using ConvenientNote.ColorPicker.Infrastructure;
using Xunit;

namespace ConvenientNote.ColorPicker.Tests;

public sealed class ColorPickerTests
{
    [Theory]
    [InlineData(0, 0, 0, "#000000", "rgb(0, 0, 0)")]
    [InlineData(255, 128, 1, "#FF8001", "rgb(255, 128, 1)")]
    public void Color_formats_and_parses_round_trip(byte r, byte g, byte b, string hex, string rgb)
    {
        var color = new ColorValue(r, g, b);
        Assert.Equal(hex, color.Hex);
        Assert.Equal(rgb, color.Rgb);
        Assert.Equal(color, ColorValue.ParseHex(hex.ToLowerInvariant()));
    }

    [Theory]
    [InlineData("#FFF")]
    [InlineData("#GG0000")]
    [InlineData("#12345678")]
    public void Invalid_colors_are_rejected(string value) => Assert.Throws<FormatException>(() => ColorValue.ParseHex(value));

    [Fact]
    public void Snapshot_uses_physical_coordinates_including_negative_monitor_origin()
    {
        var snapshot = new ScreenSnapshot(-2, -1, 2, 1, [3, 2, 1, 0, 6, 5, 4, 0]);
        Assert.Equal(new ColorValue(1, 2, 3), snapshot.Sample(-2, -1));
        Assert.Equal(new ColorValue(4, 5, 6), snapshot.Sample(-1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.Sample(0, -1));
    }

    [Fact]
    public void Snapshot_does_not_expose_mutable_original_pixels()
    {
        byte[] pixels = [3, 2, 1, 0];
        var snapshot = new ScreenSnapshot(0, 0, 1, 1, pixels);
        pixels[2] = 255;
        snapshot.CopyPixels()[2] = 100;
        Assert.Equal(new ColorValue(1, 2, 3), snapshot.Sample(0, 0));
    }

    [Fact]
    public void Failed_history_save_keeps_previously_committed_history()
    {
        var service = new ColorPickerService(new FailingStore());
        Assert.Throws<IOException>(() => service.Remember(new ColorValue(4, 5, 6)));
        Assert.Equal([new ColorValue(1, 2, 3)], service.History);
    }

    private sealed class FailingStore : IColorHistoryStore
    {
        public IReadOnlyList<ColorValue> Load() => [new ColorValue(1, 2, 3)];
        public void Save(IReadOnlyList<ColorValue> colors) => throw new IOException("Disk full");
    }

    [Fact]
    public void Locked_history_does_not_abort_startup_or_allow_overwrite()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ConvenientNote.ColorPicker.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "history.json");
        const string original = "[\"#112233\"]";
        File.WriteAllText(path, original);
        try
        {
            ColorPickerService service;
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                service = new ColorPickerService(new JsonColorHistoryStore(path));
                Assert.NotNull(service.HistoryWarning);
                Assert.False(service.CanSaveHistory);
            }
            Assert.Throws<InvalidOperationException>(() => service.Remember(new ColorValue(4, 5, 6)));
            Assert.Equal(original, File.ReadAllText(path));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Corrupt_history_is_backed_up_before_empty_history_can_be_saved()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ConvenientNote.ColorPicker.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "history.json");
        const string original = "broken json";
        File.WriteAllText(path, original);
        try
        {
            var service = new ColorPickerService(new JsonColorHistoryStore(path));
            Assert.NotNull(service.HistoryWarning);
            Assert.True(service.CanSaveHistory);
            Assert.Empty(service.History);
            var backup = Assert.Single(Directory.GetFiles(folder, "*.invalid-*"));
            Assert.Equal(original, File.ReadAllText(backup));
            service.Remember(new ColorValue(1, 2, 3));
            Assert.Equal(new ColorValue(1, 2, 3), new ColorPickerService(new JsonColorHistoryStore(path)).History[0]);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Failed_corrupt_history_backup_disables_writes_and_retains_original()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ConvenientNote.ColorPicker.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        // The source component is valid; adding a backup suffix exceeds the filesystem component limit.
        var path = Path.Combine(folder, new string('h', 225) + ".json");
        const string original = "broken json";
        File.WriteAllText(path, original);
        try
        {
            var service = new ColorPickerService(new JsonColorHistoryStore(path));
            Assert.False(service.CanSaveHistory);
            Assert.NotNull(service.HistoryWarning);
            Assert.Throws<InvalidOperationException>(() => service.Remember(new ColorValue(4, 5, 6)));
            Assert.Equal(original, File.ReadAllText(path));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Persistent_history_is_isolated_deduplicated_and_bounded()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ConvenientNote.ColorPicker.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var first = new ColorPickerService(new JsonColorHistoryStore(Path.Combine(folder, "one.json")));
            var second = new ColorPickerService(new JsonColorHistoryStore(Path.Combine(folder, "two.json")));
            for (byte i = 0; i < 40; i++) first.Remember(new ColorValue(i, 0, 0));
            first.Remember(new ColorValue(10, 0, 0));
            var restored = new ColorPickerService(new JsonColorHistoryStore(Path.Combine(folder, "one.json")));
            Assert.Equal(32, restored.History.Count);
            Assert.Equal(new ColorValue(10, 0, 0), restored.History[0]);
            Assert.Single(restored.History, x => x.R == 10);
            Assert.Empty(second.History);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
