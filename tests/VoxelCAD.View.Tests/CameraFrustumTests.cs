using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// What a camera looks like in the viewport. The drawing is the only thing that
/// says where a shot is pointed, so what it promises has to be what the render
/// delivers: the same angle, the same aspect, and a mark saying which way is up.
/// </summary>
public sealed class CameraFrustumTests
{
    private const float Aspect = 16f / 9f;
    private const float Reach = 10f;

    /// <summary>
    /// Godot's convention, which the tracer's own arithmetic restates. Drawn the
    /// other way the gizmo would point away from what the picture contains, which
    /// is the one thing it exists to say.
    /// </summary>
    [Fact]
    public void TheViewIsDrawnDownTheCamerasOwnMinusZ()
    {
        IReadOnlyList<Vector3> lines = CameraFrustum.CreateLines(CameraLens.Default, Aspect, Reach);

        Assert.Contains(lines, point => point.Z <= -Reach + 0.001f);
        Assert.DoesNotContain(lines, point => point.Z < -Reach - 0.001f);
    }

    /// <summary>
    /// The frame's height is the field of view, so a wider lens draws a wider
    /// cone. Asserted as a relation rather than a number: what matters is that the
    /// drawing follows the lens at all.
    /// </summary>
    [Theory]
    [InlineData(20f, 60f)]
    [InlineData(35f, 90f)]
    [InlineData(50f, 120f)]
    public void AWiderLensDrawsAWiderCone(float narrow, float wide)
    {
        Assert.True(Height(Lens(narrow)) < Height(Lens(wide)));
    }

    /// <summary>
    /// Half the frame is the tangent of half the angle, times the reach — the very
    /// arithmetic the tracer packs into its parameters, so the wireframe and the
    /// picture cannot disagree about what is in shot.
    /// </summary>
    [Fact]
    public void TheConeIsExactlyTheAngleTheTracerWillUse()
    {
        float expected = 2f * Reach * Mathf.Tan(Mathf.DegToRad(50f) * 0.5f);

        Assert.Equal(expected, Height(Lens(50f)), 3);
    }

    /// <summary>
    /// What the wireframe encloses has to be what the picture will contain, so the
    /// frame is drawn at the shape of the render rather than square.
    /// </summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(16f / 9f)]
    [InlineData(0.5f)]
    public void TheFrameCarriesThePicturesOwnAspect(float aspect)
    {
        IReadOnlyList<Vector3> lines =
            CameraFrustum.CreateLines(CameraLens.Default, aspect, Reach);

        float width = lines.Max(point => point.X) - lines.Min(point => point.X);

        Assert.Equal(Height(CameraLens.Default) * aspect, width, 3);
    }

    /// <summary>
    /// A parallel lens takes in the same amount at every distance, which is the
    /// whole visible difference between the two projections and the reason the
    /// drawing is worth having: its near end is the same rectangle as its far one,
    /// where a converging lens's is a point.
    /// </summary>
    [Fact]
    public void AParallelLensDrawsABoxRatherThanACone()
    {
        var lens = new CameraLens(RenderProjection.Orthographic, 50f, 8f, 0f, 0f);
        IReadOnlyList<Vector3> lines = CameraFrustum.CreateLines(lens, Aspect, Reach);

        Assert.Equal(8f, Height(lens), 3);
        Assert.Contains(lines, point => Corner(point, 4f, 4f * Aspect, 0f));
        Assert.Contains(lines, point => Corner(point, 4f, 4f * Aspect, -Reach));
        Assert.DoesNotContain(lines, point => point == Vector3.Zero);
    }

    /// <summary>A converging one starts at a point, which is what a lens is.</summary>
    [Fact]
    public void AConvergingLensStartsAtTheLens()
    {
        IReadOnlyList<Vector3> lines = CameraFrustum.CreateLines(CameraLens.Default, Aspect, Reach);

        // Every side line leaves the lens itself. The body's own corners stand
        // away from it, so nothing else in the drawing is at the origin.
        Assert.Equal(4, lines.Count(point => point == Vector3.Zero));
    }

    /// <summary>
    /// Nought means "keep the framing the converging lens had", which is what it
    /// means to the renderer — so the drawing says the same thing.
    /// </summary>
    [Fact]
    public void AParallelLensWithNoWindowKeepsTheConvergingOnesFraming()
    {
        var lens = new CameraLens(RenderProjection.Orthographic, 50f, 0f, 0f, 0f);

        Assert.Equal(Height(Lens(50f)), Height(lens), 3);
    }

    /// <summary>
    /// A frustum is symmetric about its own axis in every other respect, so
    /// without this nothing in the drawing says which way up the camera is — which
    /// is exactly the mistake a hand makes while aiming one.
    /// </summary>
    [Fact]
    public void SomethingMarksWhichWayIsUp()
    {
        IReadOnlyList<Vector3> lines = CameraFrustum.CreateLines(CameraLens.Default, Aspect, Reach);
        float halfHeight = Height(CameraLens.Default) * 0.5f;

        Assert.Contains(lines, point => point.Y > halfHeight + 0.001f);
        Assert.DoesNotContain(lines, point => point.Y < -halfHeight - 0.001f);
    }

    /// <summary>Line lists come in pairs; an odd count is a line with one end.</summary>
    [Theory]
    [InlineData(RenderProjection.Perspective)]
    [InlineData(RenderProjection.Orthographic)]
    public void EveryLineHasTwoEnds(RenderProjection projection)
    {
        IReadOnlyList<Vector3> lines = CameraFrustum.CreateLines(
            CameraLens.Default with { Projection = projection },
            Aspect,
            Reach);

        Assert.NotEmpty(lines);
        Assert.Equal(0, lines.Count % 2);
    }

    /// <summary>
    /// A gizmo is drawn from arguments a form and a file can both supply, so it
    /// must not be able to produce a degenerate mesh — the same bargain every
    /// clamp in this codebase makes.
    /// </summary>
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(-1f, -5f)]
    public void AnAbsurdAspectOrReachStillDrawsSomething(float aspect, float reach)
    {
        IReadOnlyList<Vector3> lines =
            CameraFrustum.CreateLines(CameraLens.Default, aspect, reach);

        Assert.NotEmpty(lines);
        Assert.All(lines, point => Assert.True(point.IsFinite()));
    }

    private static CameraLens Lens(float fov) => CameraLens.Default with { FovDegrees = fov };

    private static float Height(CameraLens lens)
    {
        IReadOnlyList<Vector3> lines = CameraFrustum.CreateLines(lens, Aspect, Reach);
        List<Vector3> far = lines.Where(point => point.Z <= -Reach + 0.001f).ToList();

        // The up mark peaks above the frame, so the frame's own height is measured
        // from the corners rather than from the extreme.
        float top = far.Where(point => point.X != 0f).Max(point => point.Y);

        return top - far.Min(point => point.Y);
    }

    /// <summary>Whether a point is a corner of the frame of that size at that depth.</summary>
    private static bool Corner(Vector3 point, float halfHeight, float halfWidth, float z) =>
        Mathf.IsEqualApprox(point.Z, z)
        && Mathf.IsEqualApprox(Mathf.Abs(point.Y), halfHeight)
        && Mathf.IsEqualApprox(Mathf.Abs(point.X), halfWidth);
}
