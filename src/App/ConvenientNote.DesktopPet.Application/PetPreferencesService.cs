using ConvenientNote.DesktopPet.Domain;

namespace ConvenientNote.DesktopPet.Application;

public interface IPetPreferencesStore
{
    PetPreferences Read();
    void Write(PetPreferences preferences);
}

public sealed class PetPreferencesService
{
    private readonly IPetPreferencesStore _store;
    public PetPreferences Current { get; private set; } = new();
    public string? LoadWarning { get; }

    public PetPreferencesService(IPetPreferencesStore store)
    {
        _store = store;
        try { Current = store.Read(); Current.Validate(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            Current = new();
            LoadWarning = "桌宠设置读取失败，原文件已保留：" + error.Message;
        }
    }

    public void Update(PetPreferences preferences)
    {
        if (LoadWarning is not null) throw new InvalidOperationException(LoadWarning);
        preferences.Validate();
        _store.Write(preferences);
        Current = preferences;
    }
}
