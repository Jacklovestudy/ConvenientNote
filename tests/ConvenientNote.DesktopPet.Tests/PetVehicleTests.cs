using ConvenientNote.DesktopPet.Domain;
using Xunit;

namespace ConvenientNote.DesktopPet.Tests;

public sealed class PetVehicleTests
{
    [Fact]
    public void SelectionUsesCurrentScreenAndKeepsVehicleInMiddleBand()
    {
        Assert.Equal(PetVehicle.Rocket, PetVehicleSelection.At(-700, -1000, 1000, PetVehicle.Bicycle));
        Assert.Equal(PetVehicle.Bicycle, PetVehicleSelection.At(-300, -1000, 1000, PetVehicle.Rocket));
        Assert.Equal(PetVehicle.Rocket, PetVehicleSelection.At(500, 0, 1000, PetVehicle.Rocket));
        Assert.Equal(PetVehicle.Bicycle, PetVehicleSelection.At(500, 0, 1000, PetVehicle.Bicycle));
    }

    [Fact]
    public void RocketBrakesThenTurnsWithoutThrowingRiderOff()
    {
        var motion = new PetMotion();
        motion.ChangeVehicle(PetVehicle.Rocket);
        motion.RidingSpeed = 40; motion.Boost();
        Assert.Equal(80, motion.Speed);
        motion.TurnAtEdge();
        Assert.Equal(PetAction.Brake, motion.Action);
        Assert.Equal(1, motion.Direction);
        motion.Advance(.6);
        Assert.Equal(-1, motion.Direction);
        Assert.Equal(PetAction.Ride, motion.Action);
        motion.ChangeVehicle(PetVehicle.Bicycle);
        motion.TurnAtEdge();
        Assert.Equal(PetAction.Crash, motion.Action);
    }
}
