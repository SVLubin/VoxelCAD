using System;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// Which face the plaque's robot wears. The control itself is a Godot node, and
/// the mood that matters most — stopped answering — only arrives a minute after
/// an agent goes quiet, in a window that closes again half a minute later. So
/// the mapping is held here, and what a cross-fade looks like is looked at.
/// </summary>
public sealed class AgentFaceTests
{
    [Fact]
    public void EveryMoodWearsARegisteredFace()
    {
        foreach (AgentMood mood in Enum.GetValues<AgentMood>())
            Assert.Contains(AgentFace.FaceFor(mood), EditorIconFactory.Names);
    }

    /// <summary>
    /// Three moods, three faces. Sharing one would be a state the plaque cannot
    /// say out loud — and the crossed eyes are the whole of how "it has stopped
    /// answering" is read without the subtitle.
    /// </summary>
    [Fact]
    public void NoTwoMoodsShareAFace()
    {
        Assert.Equal(
            Enum.GetValues<AgentMood>().Length,
            new System.Collections.Generic.HashSet<string>(
                Array.ConvertAll(Enum.GetValues<AgentMood>(), AgentFace.FaceFor)).Count);
    }

    /// <summary>The blink is a fourth drawing, and never a mood.</summary>
    [Fact]
    public void TheBlinkIsRegisteredAndIsNobodysMood()
    {
        Assert.Contains(EditorIconCatalog.AgentFaceBlink, EditorIconFactory.Names);

        foreach (AgentMood mood in Enum.GetValues<AgentMood>())
            Assert.NotEqual(EditorIconCatalog.AgentFaceBlink, AgentFace.FaceFor(mood));
    }

    /// <summary>Each of the four is registered again at the plaque's own strength.</summary>
    [Fact]
    public void EveryFaceHasAPlaqueRegistration() =>
        Assert.All(
            EditorIconFactory.AgentFaces,
            face => Assert.NotEqual(face, EditorIconFactory.PlaqueFace(face)));
}
