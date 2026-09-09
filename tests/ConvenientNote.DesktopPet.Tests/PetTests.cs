using ConvenientNote.DesktopPet.Domain;
using ConvenientNote.DesktopPet.Application;
using ConvenientNote.DesktopPet.Infrastructure;
using Xunit;

namespace ConvenientNote.DesktopPet.Tests;

public sealed class PetTests
{
    [Fact]
    public void BoostTracksConfiguredSpeedAndBrakingStartsAtBoostSpeed()
    {
        var pet = new PetMotion();
        pet.RidingSpeed = 40;
        Assert.Equal(40, pet.Speed);
        pet.Boost(); Assert.Equal(80, pet.Speed);
        pet.RidingSpeed = 55; Assert.Equal(110, pet.Speed);
        pet.Brake(); Assert.Equal(110, pet.Speed);
        pet.Advance(.25); Assert.Equal(55, pet.Speed);
        pet.Advance(.3); Assert.Equal(55, pet.Speed);
    }

    [Fact]
    public void OldSettingsUseOriginalSpeedAndNewSpeedRoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "pet.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, "{\"Enabled\":false,\"Scale\":1,\"Roaming\":true}");
            var service = new PetPreferencesService(new JsonPetPreferencesStore(path));
            Assert.Equal(22, service.Current.RidingSpeed);
            service.Update(service.Current with { RidingSpeed = 75 });
            Assert.Equal(75, new PetPreferencesService(new JsonPetPreferencesStore(path)).Current.RidingSpeed);
            Assert.Throws<ArgumentOutOfRangeException>(() => (service.Current with { RidingSpeed = double.NaN }).Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => new PetMotion().RidingSpeed = 0);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void EdgeCollisionFinishesBeforeTurningAndRidingBack()
    {
        var pet = new PetMotion();
        pet.TurnAtEdge();
        Assert.Equal(PetAction.Crash, pet.Action);
        Assert.Equal(0, pet.Speed);
        Assert.Equal(1, pet.Direction);
        pet.Advance(.6);
        Assert.Equal(PetAction.Crash, pet.Action);
        Assert.Equal(1, pet.Direction);
        pet.TurnAtEdge(); // Repeated contact must not restart the animation.
        Assert.Equal(.6, pet.Age);
        pet.Advance(2.5);
        Assert.Equal(-1, pet.Direction);
        Assert.Equal(PetAction.Ride, pet.Action);
    }

    [Fact]
    public void DraggingCanInterruptCollisionWithoutALateTurn()
    {
        var pet = new PetMotion();
        pet.TurnAtEdge(); pet.Advance(.8);
        pet.React(); pet.Boost();
        Assert.Equal(PetAction.Crash, pet.Action);
        pet.BeginDrag(); pet.Release(); pet.Advance(5);
        Assert.Equal(PetAction.Ride, pet.Action);
        Assert.Equal(1, pet.Direction);
    }

    [Fact]
    public void DragIsStableUntilReleaseAndResetsIdle()
    {
        var pet = new PetMotion();
        pet.BeginDrag();
        pet.Advance(120);
        Assert.Equal(PetAction.Drag, pet.Action);
        Assert.Equal(0, pet.Speed);
        pet.Release();
        Assert.Equal(PetAction.Brake, pet.Action);
        pet.Advance(1);
        Assert.Equal(PetAction.Ride, pet.Action);
    }

    [Fact]
    public void BoostSlowsAndIdleSleepsUntilInteraction()
    {
        var pet = new PetMotion();
        pet.Boost();
        Assert.True(pet.Speed > 20);
        pet.Advance(3);
        Assert.Equal(PetAction.Brake, pet.Action);
        pet.Advance(1);
        pet.Advance(100);
        Assert.Equal(PetAction.Sleep, pet.Action);
        pet.React();
        Assert.Equal(PetAction.React, pet.Action);
        pet.Advance(2);
        Assert.Equal(PetAction.Ride, pet.Action);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-2)]
    [InlineData(99)]
    public void InvalidSizeIsRejected(double scale) => Assert.Throws<ArgumentOutOfRangeException>(() => new PetPreferences(Scale: scale).Validate());

    [Fact]
    public void PreferencesRoundTripWithoutEnablingOnFirstRun()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "pet.json");
        try
        {
            var service = new PetPreferencesService(new JsonPetPreferencesStore(path));
            Assert.False(service.Current.Enabled);
            service.Update(new PetPreferences(true, 1.2, -300, 450, false));
            Assert.Equal(service.Current, new PetPreferencesService(new JsonPetPreferencesStore(path)).Current);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void FailedSaveDoesNotChangeCurrentPreferences()
    {
        var service = new PetPreferencesService(new FailingStore());
        Assert.Throws<IOException>(() => service.Update(service.Current with { Enabled = true }));
        Assert.False(service.Current.Enabled);
    }

    [Fact]
    public void CorruptPreferencesStayUntouchedAndDoNotAutoStart()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "pet.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "broken settings");
        try
        {
            var service = new PetPreferencesService(new JsonPetPreferencesStore(path));
            Assert.NotNull(service.LoadWarning);
            Assert.False(service.Current.Enabled);
            Assert.Throws<InvalidOperationException>(() => service.Update(new PetPreferences(true)));
            Assert.Equal("broken settings", File.ReadAllText(path));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    private sealed class FailingStore : IPetPreferencesStore
    {
        public PetPreferences Read() => new();
        public void Write(PetPreferences preferences) => throw new IOException("Locked");
    }
}
