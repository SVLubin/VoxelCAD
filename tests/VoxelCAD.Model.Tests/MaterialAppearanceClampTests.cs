using System;
using System.Linq;
using System.Reflection;
using Godot;
using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// The clamp rebuilds an appearance field by field, so anything it forgets to
/// name is reset to its default without a word.
/// </summary>
public class MaterialAppearanceClampTests
{
    /// <summary>
    /// Every property, compared against a value deliberately unlike the default.
    /// Walked by reflection rather than asserted field by field, so a field added
    /// tomorrow is compared the day it is added.
    /// </summary>
    [Fact]
    public void NothingItDoesNotClampIsLost()
    {
        MaterialAppearance appearance = Unusual();
        MaterialAppearance clamped = Through(appearance);

        foreach (PropertyInfo property in Fields())
            Assert.Equal(property.GetValue(appearance), property.GetValue(clamped));
    }

    /// <summary>
    /// <b>And the probe differs from the default in every field</b>, which is what
    /// makes the walk above mean anything.
    /// </summary>
    /// <remarks>
    /// The values are hand-written, because a record's positional constructor is
    /// the only way to be sure every field was named — so a field added tomorrow
    /// arrives here at <em>its own default</em>, and the walk then compares a
    /// default against a default and passes however badly the clamp drops it.
    /// That is not hypothetical: it is exactly how <c>Transparent</c> was added,
    /// and exactly the defect the clamp then shipped. This fails until somebody
    /// gives the new field a value, and its message says so.
    /// </remarks>
    [Fact]
    public void TheProbeSaysSomethingAboutEveryField()
    {
        MaterialAppearance appearance = Unusual();
        MaterialAppearance fallback = MaterialAppearance.Default;

        foreach (PropertyInfo property in Fields())
        {
            Assert.False(
                Equals(property.GetValue(appearance), property.GetValue(fallback)),
                $"'{property.Name}' is at its default in the probe, so NothingItDoesNotClampIsLost "
                + "compares a default against a default for it and cannot fail. Give it a value "
                + "in Unusual().");
        }
    }

    /// <summary>
    /// What is not a number is not a value. <c>Math.Clamp</c> is three
    /// comparisons and every comparison with a NaN is false, so it hands one
    /// straight back — and a NaN scale reaches the viewport as the divisor of
    /// every vertex of that material and takes the whole surface with it. The
    /// command surface can spell one: <c>float.TryParse</c> accepts "NaN".
    /// </summary>
    /// <remarks>
    /// Walked by reflection like the test above, so a float field added tomorrow
    /// is covered the day it is added.
    /// </remarks>
    [Fact]
    public void NothingComesOutOfItThatIsNotANumber()
    {
        MaterialAppearance clamped = Through(new MaterialAppearance(
            Roughness: float.NaN,
            Metallic: float.NaN,
            Emission: float.NaN,
            Finish: MaterialFinish.Brick,
            FinishScale: float.NaN,
            FinishRelief: float.NaN,
            Transparent: true));

        foreach (PropertyInfo property in Fields())
        {
            if (property.GetValue(clamped) is float value)
                Assert.True(float.IsFinite(value), $"'{property.Name}' came out as {value}");
        }
    }

    /// <summary>An infinity is a different case: it compares, so it clamps.</summary>
    [Fact]
    public void AnInfinityClampsToTheBoundLikeAnythingElse()
    {
        MaterialAppearance clamped = Through(
            MaterialAppearance.Default with
            {
                FinishScale = float.PositiveInfinity,
                Roughness = float.NegativeInfinity
            });

        Assert.Equal(MaterialAppearanceLimits.MaximumScale, clamped.FinishScale);
        Assert.Equal(0f, clamped.Roughness);
    }

    /// <summary>
    /// An appearance unlike the default in every single field. Kept in one place
    /// so the guard above and the walk it guards cannot come to disagree.
    /// </summary>
    private static MaterialAppearance Unusual() =>
        new(
            Roughness: 0.25f,
            Metallic: 0.5f,
            Emission: 0.75f,
            Finish: MaterialFinish.Brick,
            FinishScale: 3f,
            FinishRelief: 1.5f,
            Transparent: true);

    private static PropertyInfo[] Fields() =>
        typeof(MaterialAppearance)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.Name != "Default")
            .ToArray();

    [Fact]
    public void WhatItDoesClampItStillClamps()
    {
        MaterialAppearance clamped = Through(
            MaterialAppearance.Default with { Roughness = 5f, Metallic = -2f });

        Assert.Equal(1f, clamped.Roughness);
        Assert.Equal(0f, clamped.Metallic);
    }

    [Fact]
    public void DeclaringItSeeThroughSurvivesTheClamp()
    {
        Assert.True(Through(MaterialAppearance.Default with { Transparent = true }).Transparent);
    }

    /// <summary>
    /// Through the seam a caller actually uses, rather than into the clamp: what
    /// matters is that a material ends up wearing what it was given.
    /// </summary>
    private static MaterialAppearance Through(MaterialAppearance appearance)
    {
        IProject project = ModelFactory.CreateProject();
        Guid id = project.CreateMaterial("Probe", Colors.White);

        project.SetMaterialAppearance(id, appearance);
        project.TryGetMaterial(id, out IMaterial material);

        return material.Appearance;
    }
}
