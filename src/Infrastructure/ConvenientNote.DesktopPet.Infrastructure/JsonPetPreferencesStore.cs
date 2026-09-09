using System.Text.Json;
using ConvenientNote.DesktopPet.Application;
using ConvenientNote.DesktopPet.Domain;

namespace ConvenientNote.DesktopPet.Infrastructure;

public sealed class JsonPetPreferencesStore(string path) : IPetPreferencesStore
{
    private readonly string _path = Path.GetFullPath(path);
    public PetPreferences Read()
    {
        if (!File.Exists(_path)) return new();
        try { return JsonSerializer.Deserialize<PetPreferences>(File.ReadAllText(_path)) ?? throw new InvalidDataException("设置文件为空。"); }
        catch (JsonException error) { throw new InvalidDataException("设置文件格式无效。", error); }
    }

    public void Write(PetPreferences preferences)
    {
        preferences.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, preferences);
                file.Flush(true);
            }
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
