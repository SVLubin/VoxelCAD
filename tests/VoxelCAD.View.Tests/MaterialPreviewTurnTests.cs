using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// What a preview does with the angle it is handed. Shared by the panel's turn
/// buttons and the <c>material-preview</c> command, so a limit that lived in only
/// one of them would make an agent's picture and a user's block disagree about
/// where the poles are.
/// </summary>
public sealed class MaterialPreviewTurnTests
{
    [Fact]
    public void TheDefaultIsAThreeQuarterViewFromAbove()
    {
        MaterialPreviewTurn turn = MaterialPreviewTurn.Default;

        // Three faces at once, which is the whole reason for the angle: a facture
        // behaves differently on differently-facing surfaces and one face hides it.
        Assert.Equal(35f, turn.YawDegrees);
        Assert.Equal(-25f, turn.PitchDegrees);
    }

    [Fact]
    public void TheDefaultNeedsNoClamping()
    {
        Assert.Equal(MaterialPreviewTurn.Default, MaterialPreviewTurns.Clamp(MaterialPreviewTurn.Default));
    }

    [Fact]
    public void YawWrapsRatherThanStopping()
    {
        // Stepping round the block is what the yaw is for, so a full turn has to
        // come back to where it started instead of piling up against a limit.
        Assert.Equal(40f, MaterialPreviewTurns.Clamp(new MaterialPreviewTurn(400f, 0f)).YawDegrees);
        Assert.Equal(350f, MaterialPreviewTurns.Clamp(new MaterialPreviewTurn(-10f, 0f)).YawDegrees);
        Assert.Equal(0f, MaterialPreviewTurns.Clamp(new MaterialPreviewTurn(720f, 0f)).YawDegrees);
    }

    [Fact]
    public void PitchStopsShortOfThePoles()
    {
        // At the pole the turn has no direction left to mean — the scene camera's
        // pitch is clamped for the same reason.
        Assert.Equal(85f, MaterialPreviewTurns.Clamp(new MaterialPreviewTurn(0f, 120f)).PitchDegrees);
        Assert.Equal(-85f, MaterialPreviewTurns.Clamp(new MaterialPreviewTurn(0f, -120f)).PitchDegrees);
    }

    [Fact]
    public void AnAngleInsideTheLimitsIsLeftAlone()
    {
        var asked = new MaterialPreviewTurn(123.5f, -40f);

        Assert.Equal(asked, MaterialPreviewTurns.Clamp(asked));
    }
}
