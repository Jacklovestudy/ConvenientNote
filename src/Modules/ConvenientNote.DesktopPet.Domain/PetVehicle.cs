namespace ConvenientNote.DesktopPet.Domain;

public enum PetVehicle { Bicycle, Rocket }

public static class PetVehicleSelection
{
    public static PetVehicle At(double centerY, double screenTop, double screenHeight, PetVehicle current)
    {
        if (!double.IsFinite(centerY) || !double.IsFinite(screenTop) || !double.IsFinite(screenHeight) || screenHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(screenHeight));
        var fraction = (centerY - screenTop) / screenHeight;
        if (fraction <= .45) return PetVehicle.Rocket;
        if (fraction >= .55) return PetVehicle.Bicycle;
        return current;
    }
}
