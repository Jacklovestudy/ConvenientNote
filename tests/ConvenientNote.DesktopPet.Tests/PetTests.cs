using ConvenientNote.DesktopPet.Domain;
using ConvenientNote.DesktopPet.Application;
using ConvenientNote.DesktopPet.Infrastructure;
using Xunit;

namespace ConvenientNote.DesktopPet.Tests;

public sealed class PetTests
{
    [Fact]
    public void EdgeTurnsOnlyAfterBraking()
    {
        var pet = new PetMotion();
        pet.TurnAtEdge();
        Assert.Equal(PetAction.Brake, pet.Action);
        Assert.Equal(1, pet.Direction);
        pet.Advance(.6);
        Assert.Equal(-1, pet.Direction);
        Assert.Equal(PetAction.Ride, pet.Action);
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
