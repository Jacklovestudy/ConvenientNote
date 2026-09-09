namespace ConvenientNote.DesktopPet.Contracts;

public interface IDesktopPet
{
    bool IsVisible { get; }
    void Show();
    void Hide();
}
