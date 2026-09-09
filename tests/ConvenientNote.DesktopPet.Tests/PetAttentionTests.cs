using ConvenientNote.DesktopPet.Domain;
using Xunit;

namespace ConvenientNote.DesktopPet.Tests;

public sealed class PetAttentionTests
{
    [Fact]
    public void PassingOverHeadDoesNotWakeButLingeringDoes()
    {
        var attention = new PetAttention();
        Assert.False(attention.Advance(.1, true, true, 0, true));
        Assert.True(attention.IsHovering);
        Assert.True(attention.Advance(.7, true, true, 0, true));
        Assert.False(attention.Advance(.1, true, true, 0, true));
        attention.Advance(.1, false, false, 0, false);
        Assert.False(attention.IsHovering);
    }

    [Fact]
    public void StrokingRequiresHeadMovementAndEndsAfterLeaving()
    {
        var attention = new PetAttention();
        attention.Advance(1, true, true, 0, false);
        Assert.False(attention.IsPetting);
        attention.Advance(.1, true, true, 18, false);
        attention.Advance(.1, true, true, 18, false);
        Assert.True(attention.IsPetting);
        attention.Advance(.1, false, false, 0, false);
        Assert.False(attention.IsPetting);
        attention.Advance(.1, true, false, 100, false);
        Assert.False(attention.IsPetting);
    }
}
