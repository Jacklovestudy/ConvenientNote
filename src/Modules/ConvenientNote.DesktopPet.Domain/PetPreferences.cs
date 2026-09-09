namespace ConvenientNote.DesktopPet.Domain;

public sealed record PetPreferences(bool Enabled = false, double Scale = 1, double? Left = null, double? Top = null, bool Roaming = true, double RidingSpeed = 22)
{
    public static void ValidateSpeed(double speed)
    {
        if (!double.IsFinite(speed) || speed < 10 || speed > 120) throw new ArgumentOutOfRangeException(nameof(speed));
    }
    public void Validate()
    {
        ValidateSpeed(RidingSpeed);
        if (!double.IsFinite(Scale) || Scale < .6 || Scale > 1.6) throw new ArgumentOutOfRangeException(nameof(Scale));
        if (Left.HasValue != Top.HasValue || Left is { } x && !double.IsFinite(x) || Top is { } y && !double.IsFinite(y))
            throw new ArgumentOutOfRangeException(nameof(Left));
    }
}
