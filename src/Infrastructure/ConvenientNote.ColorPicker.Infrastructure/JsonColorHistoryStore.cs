using System.Text.Json;
using ConvenientNote.ColorPicker.Application;
using ConvenientNote.ColorPicker.Domain;

namespace ConvenientNote.ColorPicker.Infrastructure;

/// <summary>Owns only the color picker history file; does not touch workspace storage.</summary>
public sealed class JsonColorHistoryStore(string path) : IColorHistoryStore
{
    private readonly string _path = Path.GetFullPath(path);
    public string? LoadWarning { get; private set; }
    public bool CanSave { get; private set; } = true;

    public IReadOnlyList<ColorValue> Load()
    {
        try
        {
            var values = JsonSerializer.Deserialize<string[]>(File.ReadAllText(_path)) ?? [];
            return values.Select(ColorValue.ParseHex).ToArray();
        }
        catch (Exception error) when (error is JsonException or FormatException)
        {
            // Preserve the original file for diagnosis/recovery; a corrupt history must not prevent startup.
            try
            {
                File.Copy(_path, _path + ".invalid-" + Guid.NewGuid().ToString("N"));
                LoadWarning = "最近颜色文件已损坏，原文件已备份，将重新记录颜色。";
            }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
            {
                DisableWrites("最近颜色文件已损坏且无法备份");
            }
            return [];
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return []; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            DisableWrites("无法读取最近颜色，文件可能被占用或没有访问权限");
            return [];
        }
    }

    private void DisableWrites(string reason)
    {
        CanSave = false;
        LoadWarning = reason + "。原文件保持不变，本次仅支持取色和复制；恢复文件访问后请重启软件。";
    }

    public void Save(IReadOnlyList<ColorValue> colors)
    {
        if (!CanSave) throw new InvalidOperationException("最近颜色暂时只读，原文件未修改。恢复文件访问后请重启软件。");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, colors.Select(x => x.Hex).ToArray());
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
